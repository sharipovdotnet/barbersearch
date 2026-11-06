using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.RejectSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Владение салоном и модерация на реальной PostgreSQL: барбер создаёт салон через
/// <c>POST api/salons/own</c>, админ одобряет или отклоняет, владелец правит только свой салон.
/// </summary>
/// <remarks>
/// Сценарии идут через HTTP, а итоговое состояние сверяется в БД: важны не только статусы
/// ответов, но и <c>moderation_status</c>/<c>is_active</c>/<c>owner_user_id</c>, привязка
/// <c>barber_salons</c> с <c>is_primary</c> и строки аудита. Публичный каталог проверяется
/// отдельно от админского: pending не должен просачиваться в витрину, а счётчик
/// «Выключенные салоны» не должен его считать.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SalonOwnershipTests
{
    private const string SALON_DIRECTORY = "api/salons";
    private const string ADMIN_SALONS = "api/admin/catalogue/salons";

    private readonly PostgresFixture _database;

    public SalonOwnershipTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_creates_pending_salon_linked_as_primary_and_hidden_from_directory()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Own Salon Barber");
        var control = await data.CreateSalonAsync("Directory Control Salon");

        await app.AuthenticateAsync(owner.Email);

        var salonId = await CreateOwnSalonAsync(app, "Pending Barber Salon");

        var salon = await ReadSalonAsync(app, salonId);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(owner.UserId, salon.OwnerUserId);
        Assert.Null(salon.ReviewedAt);
        Assert.Null(salon.ReviewedByUserId);
        Assert.Null(salon.RejectionReason);

        var link = Assert.Single(await data.GetSalonLinksAsync(owner.ProfileId));
        Assert.Equal(salonId, link.SalonId);
        Assert.True(link.IsPrimary);

        var directoryIds = await ReadDirectoryIdsAsync(app);
        Assert.Contains(control.Id, directoryIds);
        Assert.DoesNotContain(salonId, directoryIds);

        var summary = await ReadSalonSummaryAsync(app, owner.ProfileId, salonId);
        Assert.Equal("Pending", summary.GetProperty("moderationStatus").GetString());
        Assert.True(summary.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("rejectionReason").ValueKind);
    }

    [Fact(Timeout = 180_000)]
    public async Task Approval_publishes_salon_and_writes_moderation_audit_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Approved Salon Barber");

        await app.AuthenticateAsync(owner.Email);
        var salonId = await CreateOwnSalonAsync(app, "Approved Barber Salon");
        Assert.DoesNotContain(salonId, await ReadDirectoryIdsAsync(app));

        var admin = await SignInAsAdminAsync(app);

        using (var approval = await app.Client.PostAsync(
                   $"api/salons/{salonId}/approval",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, approval.StatusCode);
        }

        using (var repeated = await app.Client.PostAsync(
                   $"api/salons/{salonId}/approval",
                   null))
        {
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
            Assert.Contains(
                "salon.review.invalidStatus",
                await repeated.Content.ReadAsStringAsync());
        }

        var salon = await ReadSalonAsync(app, salonId);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Equal(admin.Id, salon.ReviewedByUserId);
        Assert.NotNull(salon.ReviewedAt);

        Assert.Contains(salonId, await ReadDirectoryIdsAsync(app));

        await app.AuthenticateAsync(owner.Email);
        var summary = await ReadSalonSummaryAsync(app, owner.ProfileId, salonId);
        Assert.Equal("Active", summary.GetProperty("moderationStatus").GetString());

        var auditRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "salon"
                && row.EntityId == salonId
                && row.Action == "salon.updated");
        Assert.Equal(admin.Id, auditRow.ActorUserId);

        using var payload = JsonDocument.Parse(auditRow.Payload);
        var change = payload.RootElement
            .GetProperty("changes")
            .GetProperty("moderationStatus");

        Assert.Equal("Pending", change.GetProperty("from").GetString());
        Assert.Equal("Active", change.GetProperty("to").GetString());
    }

    [Fact(Timeout = 240_000)]
    public async Task Location_and_contacts_are_editable_by_owner_and_admin_only()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Editable Salon Barber");

        await app.AuthenticateAsync(owner.Email);
        var salonId = await CreateOwnSalonAsync(app, "Editable Pending Salon");

        app.Client.DefaultRequestHeaders.Authorization = null;

        using (var anonymousLocation = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/location",
                   NewLocationRequest()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousLocation.StatusCode);
        }

        using (var anonymousContacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/contact-info",
                   NewContactRequest()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousContacts.StatusCode);
        }

        await app.AuthenticateAsync(owner.Email);

        using (var ownerLocation = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/location",
                   NewLocationRequest()))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerLocation.StatusCode);
        }

        using (var ownerContacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/contact-info",
                   NewContactRequest()))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerContacts.StatusCode);
        }

        var salon = await ReadSalonAsync(app, salonId);
        Assert.Equal("Neue Strasse 1", salon.Address);
        Assert.Equal("+4930123456", salon.PhoneNumber);

        var otherEmail = $"salon-owner-other-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(otherEmail, UserRole.Barber);
        await app.AuthenticateAsync(otherEmail);

        await AssertForbiddenAsync(app, salonId);

        var customerEmail = $"salon-owner-customer-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(customerEmail);
        await app.AuthenticateAsync(customerEmail);

        await AssertForbiddenAsync(app, salonId);

        var admin = await SignInAsAdminAsync(app);

        using (var adminLocation = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/location",
                   NewLocationRequest("Admin Strasse 1")))
        {
            Assert.Equal(HttpStatusCode.NoContent, adminLocation.StatusCode);
        }

        using (var adminContacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/contact-info",
                   new UpdateSalonContactInfoRequest("+4930999999", null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, adminContacts.StatusCode);
        }

        salon = await ReadSalonAsync(app, salonId);
        Assert.Equal("Admin Strasse 1", salon.Address);
        Assert.Equal("+4930999999", salon.PhoneNumber);

        // Салон администратора без владельца: барбер не получает к нему доступа, админ — да.
        var ownerless = await data.CreateSalonAsync("Ownerless Salon");
        Assert.Null(ownerless.OwnerUserId);

        await app.AuthenticateAsync(owner.Email);
        await AssertForbiddenAsync(app, ownerless.Id);

        await app.AuthenticateAsync(admin.Email);

        using (var ownerlessLocation = await app.Client.PutAsJsonAsync(
                   $"api/salons/{ownerless.Id}/location",
                   NewLocationRequest("Ownerless Strasse 1")))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerlessLocation.StatusCode);
        }

        using (var ownerlessContacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{ownerless.Id}/contact-info",
                   NewContactRequest()))
        {
            Assert.Equal(HttpStatusCode.NoContent, ownerlessContacts.StatusCode);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Activation_and_rejection_enforce_moderation_and_write_audit_rows()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Moderated Salon Barber");

        await app.AuthenticateAsync(owner.Email);

        var rejectedSalonId = await CreateOwnSalonAsync(app, "Rejected Barber Salon");
        var approvedSalonId = await CreateOwnSalonAsync(app, "Second Approved Salon");

        var admin = await SignInAsAdminAsync(app);

        using (var activation = await app.Client.PostAsync(
                   $"api/salons/{rejectedSalonId}/activation",
                   null))
        {
            Assert.Equal(HttpStatusCode.Conflict, activation.StatusCode);
            Assert.Contains(
                "salon.activation.notApproved",
                await activation.Content.ReadAsStringAsync());
        }

        using (var emptyReason = await app.Client.PostAsJsonAsync(
                   $"api/salons/{rejectedSalonId}/rejection",
                   new RejectSalonRequest("   ")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, emptyReason.StatusCode);
            Assert.Contains(
                "salon.reject.reasonRequired",
                await emptyReason.Content.ReadAsStringAsync());
        }

        using (var tooLongReason = await app.Client.PostAsJsonAsync(
                   $"api/salons/{rejectedSalonId}/rejection",
                   new RejectSalonRequest(new string('a', 501))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooLongReason.StatusCode);
            Assert.Contains(
                "salon.reject.reasonTooLong",
                await tooLongReason.Content.ReadAsStringAsync());
        }

        using (var rejection = await app.Client.PostAsJsonAsync(
                   $"api/salons/{rejectedSalonId}/rejection",
                   new RejectSalonRequest("  Нет подтверждённого адреса  ")))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        var rejected = await ReadSalonAsync(app, rejectedSalonId);
        Assert.Equal(SalonModerationStatus.Rejected, rejected.ModerationStatus);
        Assert.False(rejected.IsActive);
        Assert.Equal("Нет подтверждённого адреса", rejected.RejectionReason);
        Assert.Equal(admin.Id, rejected.ReviewedByUserId);
        Assert.NotNull(rejected.ReviewedAt);
        Assert.DoesNotContain(rejectedSalonId, await ReadDirectoryIdsAsync(app));

        using (var repeatedRejection = await app.Client.PostAsJsonAsync(
                   $"api/salons/{rejectedSalonId}/rejection",
                   new RejectSalonRequest("Ещё раз")))
        {
            Assert.Equal(HttpStatusCode.Conflict, repeatedRejection.StatusCode);
            Assert.Contains(
                "salon.review.invalidStatus",
                await repeatedRejection.Content.ReadAsStringAsync());
        }

        using (var approveRejected = await app.Client.PostAsync(
                   $"api/salons/{rejectedSalonId}/approval",
                   null))
        {
            Assert.Equal(HttpStatusCode.Conflict, approveRejected.StatusCode);
            Assert.Contains(
                "salon.review.invalidStatus",
                await approveRejected.Content.ReadAsStringAsync());
        }

        using (var approval = await app.Client.PostAsync(
                   $"api/salons/{approvedSalonId}/approval",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, approval.StatusCode);
        }

        using (var repeatedApproval = await app.Client.PostAsync(
                   $"api/salons/{approvedSalonId}/approval",
                   null))
        {
            Assert.Equal(HttpStatusCode.Conflict, repeatedApproval.StatusCode);
            Assert.Contains(
                "salon.review.invalidStatus",
                await repeatedApproval.Content.ReadAsStringAsync());
        }

        Assert.Contains(approvedSalonId, await ReadDirectoryIdsAsync(app));

        var rejectionRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            row => row.EntityType == "salon"
                && row.EntityId == rejectedSalonId
                && row.Action == "salon.updated");
        Assert.Equal(admin.Id, rejectionRow.ActorUserId);

        using var rejectionPayload = JsonDocument.Parse(rejectionRow.Payload);
        var rejectionChange = rejectionPayload.RootElement
            .GetProperty("changes")
            .GetProperty("moderationStatus");

        Assert.Equal("Pending", rejectionChange.GetProperty("from").GetString());
        Assert.Equal("Rejected", rejectionChange.GetProperty("to").GetString());
    }

    [Fact(Timeout = 240_000)]
    public async Task Admin_pending_filter_and_summary_ignore_pending_salons()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await CreateOwnerAsync(app, data, "Pending Filter Barber");

        await app.AuthenticateAsync(owner.Email);
        var pendingId = await CreateOwnSalonAsync(app, "Pending Filter Salon");

        var active = await data.CreateSalonAsync("Active Filter Salon");
        var hidden = await data.CreateSalonAsync("Hidden Filter Salon", isActive: false);

        await SignInAsAdminAsync(app);

        var pendingRows = await ReadRowsAsync(app, $"{ADMIN_SALONS}?status=pending");
        var pendingRow = Assert.Single(pendingRows);
        Assert.Equal(pendingId, pendingRow.GetProperty("id").GetGuid());
        Assert.False(pendingRow.GetProperty("isActive").GetBoolean());
        Assert.Equal("Pending", pendingRow.GetProperty("moderationStatus").GetString());
        Assert.Equal(
            owner.Email,
            pendingRow.GetProperty("ownerEmail").GetString(),
            ignoreCase: true);

        var activeIds = Ids(await ReadRowsAsync(app, $"{ADMIN_SALONS}?status=active"));
        Assert.Equal([active.Id], activeIds);
        Assert.DoesNotContain(pendingId, activeIds);

        var hiddenIds = Ids(await ReadRowsAsync(app, $"{ADMIN_SALONS}?status=hidden"));
        Assert.Equal([hidden.Id], hiddenIds);
        Assert.DoesNotContain(pendingId, hiddenIds);

        var allIds = Ids(await ReadRowsAsync(app, $"{ADMIN_SALONS}?status=all"));
        Assert.Contains(pendingId, allIds);
        Assert.Contains(active.Id, allIds);
        Assert.Contains(hidden.Id, allIds);

        // Pending неактивен, но он не «выключен администратором»: сводка считает только
        // Active-салоны с is_active = false.
        Assert.Equal(1, await ReadInactiveSalonCountAsync(app));

        foreach (var url in new[]
                 {
                     "api/admin/catalogue/hair-services?status=pending",
                     "api/admin/catalogue/specializations?status=pending"
                 })
        {
            using var response = await app.Client.GetAsync(url);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "admin.catalogue.status",
                await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task<Owner> CreateOwnerAsync(
        TestApp app,
        TestData data,
        string displayName)
    {
        var email = $"salon-owner-{Guid.NewGuid():N}@barbersearch.test";
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
        using var response = await TestApp.PostJsonAsync(
            app.Client,
            "api/salons/own",
            NewSalonRequest(name));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    private static async Task<User> SignInAsAdminAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"salon-owner-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        return admin;
    }

    private static async Task AssertForbiddenAsync(TestApp app, Guid salonId)
    {
        using var location = await app.Client.PutAsJsonAsync(
            $"api/salons/{salonId}/location",
            NewLocationRequest());
        Assert.Equal(HttpStatusCode.Forbidden, location.StatusCode);

        using var contacts = await app.Client.PutAsJsonAsync(
            $"api/salons/{salonId}/contact-info",
            NewContactRequest());
        Assert.Equal(HttpStatusCode.Forbidden, contacts.StatusCode);
    }

    private static async Task<Salon> ReadSalonAsync(TestApp app, Guid salonId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Salons
            .AsNoTracking()
            .SingleAsync(salon => salon.Id == salonId);
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

    private static async Task<JsonElement> ReadSalonSummaryAsync(
        TestApp app,
        Guid barberProfileId,
        Guid salonId)
    {
        using var response = await app.Client.GetAsync(
            $"api/barber-profiles/{barberProfileId}/salons");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.EnumerateArray().ToList();
        var row = Assert.Single(
            rows,
            candidate => candidate.GetProperty("salonId").GetGuid() == salonId);

        return row.Clone();
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadRowsAsync(
        TestApp app,
        string url)
    {
        using var response = await app.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToList();
    }

    private static async Task<int> ReadInactiveSalonCountAsync(TestApp app)
    {
        using var response = await app.Client.GetAsync("api/admin/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .GetProperty("salons")
            .GetProperty("inactiveCount")
            .GetInt32();
    }

    private static Guid[] Ids(IReadOnlyList<JsonElement> rows)
        => rows.Select(row => row.GetProperty("id").GetGuid()).ToArray();

    private static CreateSalonRequest NewSalonRequest(string name)
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
            null);

    private static UpdateSalonLocationRequest NewLocationRequest(
        string address = "Neue Strasse 1")
        => new(
            address,
            "Potsdam",
            "Germany",
            "Europe/Berlin",
            52.3906,
            13.0645,
            null);

    private static UpdateSalonContactInfoRequest NewContactRequest()
        => new("+4930123456", "https://salon.example/");

    private sealed record Owner(Guid UserId, Guid ProfileId, string Email);
}
