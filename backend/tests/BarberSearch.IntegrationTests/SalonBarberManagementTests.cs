using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.AddSalonBarber;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Состав салона на реальной PostgreSQL: владелец, автоматически записанный при создании
/// салона, читает список, ищет и прикрепляет других барберов, а чужой салон закрыт.
/// </summary>
/// <remarks>
/// Сценарии идут через HTTP и сверяют не только коды ответов, но и строки
/// <c>barber_salons</c>: привязка — это связь, а не поле ответа. Первый салон
/// прикреплённого барбера становится основным, иначе у мастера не было бы салона
/// для слотов и формы записи. Владельца салона убрать нельзя: его привязка создана
/// вместе с салоном.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SalonBarberManagementTests
{
    private readonly PostgresFixture _database;

    public SalonBarberManagementTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Owner_creates_salon_then_lists_searches_attaches_and_detaches_barbers()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Salon Owner");

        await app.AuthenticateAsync(owner.Email);
        var salonId = await CreateOwnSalonAsync(app, "Owner Managed Salon");

        var inviteeEmail = $"salon-invitee-{Guid.NewGuid():N}@barbersearch.test";
        var invitee = await data.CreateBarberProfileAsync(
            "Invitable Salon Barber",
            inviteeEmail);

        var initial = await ReadBarbersAsync(app, salonId);
        var ownerRow = Assert.Single(initial);
        Assert.Equal(owner.ProfileId, ownerRow.GetProperty("barberProfileId").GetGuid());
        Assert.Equal(owner.UserId, ownerRow.GetProperty("userId").GetGuid());

        var candidates = await SearchCandidatesAsync(app, salonId, "Invitable");
        var candidate = Assert.Single(candidates);
        Assert.Equal(invitee.Id, candidate.GetProperty("barberProfileId").GetGuid());
        Assert.Equal(
            invitee.DisplayName.Value,
            candidate.GetProperty("displayName").GetString());

        using (var attach = await app.Client.PostAsJsonAsync(
                   $"api/salons/{salonId}/barbers",
                   new AddSalonBarberRequest(invitee.Id)))
        {
            Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);
        }

        var links = await data.GetSalonLinksAsync(invitee.Id);
        var link = Assert.Single(links);
        Assert.Equal(salonId, link.SalonId);
        Assert.True(link.IsPrimary);

        await app.AuthenticateAsync(owner.Email);

        // Прикреплённый профиль больше не предлагается как кандидат (NOT EXISTS).
        Assert.Empty(await SearchCandidatesAsync(app, salonId, "Invitable"));

        // Прикреплённый барбер видит салон в «Моих салонах» и может настроить расписание.
        await app.AuthenticateAsync(inviteeEmail);
        using (var ownSalons = await app.Client.GetAsync(
                   $"api/barber-profiles/{invitee.Id}/salons"))
        {
            Assert.Equal(HttpStatusCode.OK, ownSalons.StatusCode);

            using var document = JsonDocument.Parse(
                await ownSalons.Content.ReadAsStringAsync());
            var row = Assert.Single(
                document.RootElement.EnumerateArray(),
                item => item.GetProperty("salonId").GetGuid() == salonId);

            Assert.True(row.GetProperty("isPrimary").GetBoolean());
            Assert.Equal(
                owner.UserId,
                row.GetProperty("ownerUserId").GetGuid());
        }

        await app.AuthenticateAsync(owner.Email);

        var afterAttach = await ReadBarbersAsync(app, salonId);
        Assert.Equal(2, afterAttach.Count);
        Assert.Contains(
            afterAttach,
            row => row.GetProperty("barberProfileId").GetGuid() == invitee.Id);

        using (var duplicate = await app.Client.PostAsJsonAsync(
                   $"api/salons/{salonId}/barbers",
                   new AddSalonBarberRequest(invitee.Id)))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains(
                "barberProfile.addSalon.duplicate",
                await duplicate.Content.ReadAsStringAsync());
        }

        using (var removeOwner = await app.Client.DeleteAsync(
                   $"api/salons/{salonId}/barbers/{owner.ProfileId}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, removeOwner.StatusCode);
            Assert.Contains(
                "salon.barbers.owner",
                await removeOwner.Content.ReadAsStringAsync());
        }

        using (var detach = await app.Client.DeleteAsync(
                   $"api/salons/{salonId}/barbers/{invitee.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, detach.StatusCode);
        }

        Assert.Empty(await data.GetSalonLinksAsync(invitee.Id));
    }

    [Fact(Timeout = 240_000)]
    public async Task Salon_barber_management_is_closed_for_strangers_and_allows_admin()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Access Owner");

        await app.AuthenticateAsync(owner.Email);
        var salonId = await CreateOwnSalonAsync(app, "Closed Managed Salon");

        var strangerEmail = $"salon-stranger-{Guid.NewGuid():N}@barbersearch.test";
        var stranger = await data.CreateBarberProfileAsync(
            "Stranger Salon Barber",
            strangerEmail);
        var inactive = await data.CreateBarberProfileAsync(
            "Inactive Salon Barber",
            $"salon-inactive-{Guid.NewGuid():N}@barbersearch.test");
        await data.SetBarberProfileStateAsync(inactive.Id, isVerified: true, isActive: false);

        app.Client.DefaultRequestHeaders.Authorization = null;

        using (var anonymous = await app.Client.GetAsync($"api/salons/{salonId}/barbers"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await app.AuthenticateAsync(strangerEmail);

        using (var list = await app.Client.GetAsync($"api/salons/{salonId}/barbers"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }

        using (var search = await app.Client.GetAsync(
                   $"api/salons/{salonId}/barber-candidates?search=Inactive"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        }

        using (var attach = await app.Client.PostAsJsonAsync(
                   $"api/salons/{salonId}/barbers",
                   new AddSalonBarberRequest(stranger.Id)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, attach.StatusCode);
        }

        using (var detach = await app.Client.DeleteAsync(
                   $"api/salons/{salonId}/barbers/{owner.ProfileId}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, detach.StatusCode);
        }

        await app.AuthenticateAsync(owner.Email);

        // Выключенный профиль не предлагается в подсказке поиска.
        Assert.Empty(await SearchCandidatesAsync(app, salonId, "Inactive"));

        using (var inactiveAttach = await app.Client.PostAsJsonAsync(
                   $"api/salons/{salonId}/barbers",
                   new AddSalonBarberRequest(inactive.Id)))
        {
            Assert.Equal(HttpStatusCode.Conflict, inactiveAttach.StatusCode);
            Assert.Contains(
                "salon.barbers.inactive",
                await inactiveAttach.Content.ReadAsStringAsync());
        }

        using (var tooShort = await app.Client.GetAsync(
                   $"api/salons/{salonId}/barber-candidates?search=a"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
            Assert.Contains(
                "salon.barbers.search.tooShort",
                await tooShort.Content.ReadAsStringAsync());
        }

        using (var tooLong = await app.Client.GetAsync(
                   $"api/salons/{salonId}/barber-candidates?search={new string('a', 101)}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains(
                "salon.barbers.search.tooLong",
                await tooLong.Content.ReadAsStringAsync());
        }

        var admin = await app.CreateUserAsync(
            $"salon-barber-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var adminList = await app.Client.GetAsync($"api/salons/{salonId}/barbers"))
        {
            Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        }

        using (var adminAttach = await app.Client.PostAsJsonAsync(
                   $"api/salons/{salonId}/barbers",
                   new AddSalonBarberRequest(stranger.Id)))
        {
            Assert.Equal(HttpStatusCode.NoContent, adminAttach.StatusCode);
        }

        using (var adminDetach = await app.Client.DeleteAsync(
                   $"api/salons/{salonId}/barbers/{stranger.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, adminDetach.StatusCode);
        }

        Assert.Empty(await data.GetSalonLinksAsync(stranger.Id));
    }

    private static async Task<Owner> CreateOwnerAsync(
        TestApp app,
        TestData data,
        string displayName)
    {
        var email = $"salon-barber-owner-{Guid.NewGuid():N}@barbersearch.test";
        var profile = await data.CreateBarberProfileAsync(displayName, email);

        await LinkBarberProfileAsync(app, profile.UserId, profile.Id);

        return new Owner(profile.UserId, profile.Id, email);
    }

    private static async Task LinkBarberProfileAsync(
        TestApp app,
        Guid userId,
        Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        var linked = user.LinkBarberProfile(barberProfileId);

        if (linked.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot link barber profile '{barberProfileId}': "
                + string.Join("; ", linked.Error.Errors.Select(error => error.Code)));
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateOwnSalonAsync(TestApp app, string name)
    {
        using var response = await app.Client.PostAsJsonAsync(
            "api/salons/own",
            new CreateSalonRequest(
                name,
                "Teststrasse 1",
                "Berlin",
                "Germany",
                "Europe/Berlin",
                52.5200,
                13.4050,
                null,
                null,
                null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadBarbersAsync(
        TestApp app,
        Guid salonId)
    {
        using var response = await app.Client.GetAsync($"api/salons/{salonId}/barbers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToList();
    }

    private static async Task<IReadOnlyList<JsonElement>> SearchCandidatesAsync(
        TestApp app,
        Guid salonId,
        string search)
    {
        using var response = await app.Client.GetAsync(
            $"api/salons/{salonId}/barber-candidates?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToList();
    }

    private sealed record Owner(Guid UserId, Guid ProfileId, string Email);
}
