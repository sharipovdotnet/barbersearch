using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Domain.Salons;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Тип места (<see cref="PlaceKind"/>) и скрытые места на реальной PostgreSQL: барбер создаёт
/// частное место без модерации, оно сразу видно с карточки, участвует в слотах и брони,
/// но не попадает в публичный каталог салонов.
/// </summary>
/// <remarks>
/// Сценарий идёт через HTTP и проверяет именно то, что публичные чтения <b>не</b> менялись:
/// поиск по городу, карточка, слоты и бронь уже пропускают скрытое место, потому что оно
/// <c>Active</c> и <c>IsActive</c>; отдельный фильтр есть только у каталога (<c>IsListed</c>).
/// Публичный вид места и путь модерации проверяются на одном и том же хосте, чтобы правило
/// «публичное — Pending, скрытое — Active» было видно в одном прогоне.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SalonPlaceKindTests
{
    private const string SALON_DIRECTORY = "api/salons";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public SalonPlaceKindTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Unlisted_home_place_is_visible_from_the_card_and_bookable_but_not_in_directory()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await CreateOwnerAsync(app, data, "Private Home Barber");

        var hairService = await data.CreateHairServiceAsync("Private Home Cut");
        await data.AttachHairServiceAsync(barber.ProfileId, hairService.Id);
        var barberServiceId = await ReadBarberServiceIdAsync(
            app,
            barber.ProfileId,
            hairService.Id);

        var control = await data.CreateSalonAsync("Directory Control Salon", city: "Berlin");

        await app.AuthenticateAsync(barber.Email);

        var salonId = await CreateOwnSalonAsync(
            app,
            "Home Visit Place",
            PlaceKind.Home,
            isListed: false);

        // Домен: скрытое место не модерируется, но активно — поэтому публичные чтения
        // пропускают его без правки запросов.
        var salon = await ReadSalonAsync(app, salonId);
        Assert.Equal(PlaceKind.Home, salon.Kind);
        Assert.False(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Null(salon.ReviewedAt);
        Assert.Null(salon.ReviewedByUserId);
        Assert.Equal(barber.UserId, salon.OwnerUserId);

        var link = Assert.Single(await data.GetSalonLinksAsync(barber.ProfileId));
        Assert.Equal(salonId, link.SalonId);
        Assert.True(link.IsPrimary);

        // Карточка барбера показывает место и его тип.
        using (var card = await app.Client.GetAsync($"api/barbers/{barber.ProfileId}"))
        {
            Assert.Equal(HttpStatusCode.OK, card.StatusCode);

            using var document = JsonDocument.Parse(await card.Content.ReadAsStringAsync());
            var salons = document.RootElement.GetProperty("salons").EnumerateArray().ToList();
            var cardSalon = Assert.Single(
                salons,
                candidate => candidate.GetProperty("salonId").GetGuid() == salonId);

            Assert.Equal("Home", cardSalon.GetProperty("kind").GetString());
        }

        // Поиск по городу находит барбера без модерации места.
        using (var search = await app.Client.GetAsync("api/barbers?city=Berlin"))
        {
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);

            using var document = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            var items = document.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .ToList();

            var barberRow = Assert.Single(
                items,
                item => item.GetProperty("id").GetGuid() == barber.ProfileId);

            // Подпись результата поиска: тип места основного активного салона.
            Assert.Equal("Home", barberRow.GetProperty("primaryPlaceKind").GetString());
            Assert.Equal("Berlin", barberRow.GetProperty("city").GetString());
        }

        // Слоты считаются в поясе места: ответ 200, а не 404 «нет салона».
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(30));

        using (var slots = await app.Client.GetAsync(
                   $"api/barbers/{barber.ProfileId}/slots?date={date:yyyy-MM-dd}"))
        {
            Assert.Equal(HttpStatusCode.OK, slots.StatusCode);

            using var document = JsonDocument.Parse(await slots.Content.ReadAsStringAsync());
            Assert.Equal(salonId, document.RootElement.GetProperty("salonId").GetGuid());
        }

        // И записаться можно: бронь принимает место, привязанное к профилю.
        var customerEmail = $"place-kind-customer-{Guid.NewGuid():N}@barbersearch.test";
        var customerId = await app.RegisterAsync(customerEmail);
        Assert.NotEqual(Guid.Empty, customerId);
        await app.AuthenticateAsync(customerEmail);

        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(30).AddHours(10), TimeSpan.Zero);

        using (var booking = await PostJsonAsync(
                   app,
                   "api/bookings",
                   new CreateBookingRequest(
                       barber.ProfileId,
                       barberServiceId,
                       salonId,
                       start,
                       start.AddMinutes(30),
                       $"place-kind-{Guid.NewGuid():N}",
                       "private home visit")))
        {
            Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        }

        // Каталог для выбора по-прежнему только перечисленные места.
        var directoryIds = await ReadDirectoryIdsAsync(app);
        Assert.Contains(control.Id, directoryIds);
        Assert.DoesNotContain(salonId, directoryIds);
    }

    [Fact(Timeout = 240_000)]
    public async Task Listed_place_keeps_moderation_and_listed_home_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await CreateOwnerAsync(app, data, "Listed Place Barber");

        await app.AuthenticateAsync(barber.Email);

        using (var listedHome = await PostJsonAsync(
                   app,
                   "api/salons/own",
                   NewSalonRequest("Listed Home", PlaceKind.Home, isListed: true)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, listedHome.StatusCode);

            var body = await listedHome.Content.ReadAsStringAsync();
            Assert.Contains("salon.isListed.kind", body, StringComparison.Ordinal);
        }

        // Отказ валидации ничего не создал: ни салона, ни привязки.
        Assert.Empty(await data.GetSalonLinksAsync(barber.ProfileId));

        var pendingSalonId = await CreateOwnSalonAsync(
            app,
            "Regular Barbershop",
            PlaceKind.Salon,
            isListed: true);

        var pending = await ReadSalonAsync(app, pendingSalonId);
        Assert.Equal(PlaceKind.Salon, pending.Kind);
        Assert.True(pending.IsListed);
        Assert.Equal(SalonModerationStatus.Pending, pending.ModerationStatus);
        Assert.False(pending.IsActive);
        Assert.Null(pending.ReviewedAt);

        // Pending не попадает ни в каталог выбора, ни в поиск по городу.
        Assert.DoesNotContain(pendingSalonId, await ReadDirectoryIdsAsync(app));

        using (var search = await app.Client.GetAsync("api/barbers?city=Berlin"))
        {
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);

            using var document = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            var ids = document.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid())
                .ToList();

            Assert.DoesNotContain(barber.ProfileId, ids);
        }
    }

    private static async Task<Owner> CreateOwnerAsync(
        TestApp app,
        TestData data,
        string displayName)
    {
        var email = $"place-kind-owner-{Guid.NewGuid():N}@barbersearch.test";
        var profile = await data.CreateBarberProfileAsync(displayName, email);

        await LinkBarberProfileAsync(app, profile.UserId, profile.Id);

        return new Owner(profile.UserId, profile.Id, email);
    }

    /// <summary>
    /// Привязывает профиль к аккаунту: claim <c>barber_profile_id</c> появляется только после
    /// сохранения связи, поэтому логин идёт после неё.
    /// </summary>
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

    private static async Task<Guid> CreateOwnSalonAsync(
        TestApp app,
        string name,
        PlaceKind kind,
        bool isListed)
    {
        using var response = await PostJsonAsync(
            app,
            "api/salons/own",
            NewSalonRequest(name, kind, isListed));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    private static Task<HttpResponseMessage> PostJsonAsync(
        TestApp app,
        string url,
        object body)
        => app.Client.PostAsync(url, JsonContent.Create(body, options: JsonOptions));

    private static async Task<Salon> ReadSalonAsync(TestApp app, Guid salonId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Salons
            .AsNoTracking()
            .SingleAsync(salon => salon.Id == salonId);
    }

    private static async Task<Guid> ReadBarberServiceIdAsync(
        TestApp app,
        Guid barberProfileId,
        Guid hairServiceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BarberServices
            .AsNoTracking()
            .Where(service => service.BarberProfileId == barberProfileId
                              && service.HairServiceId == hairServiceId)
            .Select(service => service.Id)
            .SingleAsync();
    }

    private static async Task<IReadOnlyList<Guid>> ReadDirectoryIdsAsync(TestApp app)
    {
        using var response = await app.Client.GetAsync(SALON_DIRECTORY);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetGuid())
            .ToList();
    }

    private static CreateSalonRequest NewSalonRequest(
        string name,
        PlaceKind kind,
        bool isListed)
        => new(
            name,
            "Teststrasse 1",
            "Berlin",
            "Germany",
            "Europe/Berlin",
            52.5200,
            13.4050,
            null,
            null,
            null,
            kind,
            isListed);

    private sealed record Owner(Guid UserId, Guid ProfileId, string Email);
}
