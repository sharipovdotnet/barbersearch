using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.CreateHairService;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админское чтение журнала изменений (<c>GET api/admin/audit</c>) на реальной PostgreSQL:
/// keyset-порядок, фильтры типа, автора и периода, разбор <c>payload</c>, коды ошибок
/// и честная граница «каталог и салоны не аудируются».
/// </summary>
/// <remarks>
/// Строки аудита — инфраструктурная запись без доменных правил, поэтому они засеваются
/// SQL-вставкой (см. <see cref="TestData.SeedAuditAsync"/>). Доменные сущности и акторы
/// по-прежнему создаются через <see cref="TestApp"/>.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminAuditReadTests
{
    private const string ENDPOINT = "api/admin/audit";

    private const string IS_ACTIVE_CHANGE = """
        {"changes":{"isActive":{"from":true,"to":false}}}
        """;

    private readonly PostgresFixture _database;

    /// <summary>
    /// API читает перечисления строками: без конвертера <c>category</c> ушёл бы числом
    /// и model binding вернул бы <c>400</c> до обработчика.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminAuditReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_gets_401_and_non_admin_gets_403()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymous = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customer = await app.CreateUserAsync(
            $"audit-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var customerResponse = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);

        var barber = await app.CreateUserAsync(
            $"audit-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using var barberResponse = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.Forbidden, barberResponse.StatusCode);

        var admin = await app.CreateUserAsync(
            $"audit-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var adminResponse = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Newest_entries_come_first_across_pages_without_duplicates()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-page-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var start = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var seeded = new List<AuditSeed>();

        for (var minute = 0; minute < 15; minute++)
        {
            var at = start.AddMinutes(minute);
            var id = await data.SeedAuditAsync(
                at,
                admin.Id,
                "user.updated",
                "user",
                Guid.NewGuid(),
                IS_ACTIVE_CHANGE);
            seeded.Add(new AuditSeed(id, at));
        }

        // Десять строк одного SaveChanges: одинаковый occurred_at, порядок решает id.
        for (var index = 0; index < 10; index++)
        {
            var at = start.AddMinutes(30);
            var id = await data.SeedAuditAsync(
                at,
                admin.Id,
                "review.updated",
                "review",
                Guid.NewGuid(),
                IS_ACTIVE_CHANGE);
            seeded.Add(new AuditSeed(id, at));
        }

        var expected = seeded
            .OrderByDescending(seed => seed.OccurredAt)
            .ThenBy(seed => seed.Id)
            .Select(seed => seed.Id)
            .ToList();

        await app.AuthenticateAsync(admin.Email);

        var collected = new List<Guid>();
        string? cursor = null;

        for (var page = 0; page < 10; page++)
        {
            var url = cursor is null
                ? $"{ENDPOINT}?pageSize=10"
                : $"{ENDPOINT}?pageSize=10&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await app.Client.GetAsync(url);
            var body = await ReadPageAsync(response);

            Assert.InRange(body.Items.Count, 1, 10);
            collected.AddRange(body.Items.Select(item => item.Id));

            cursor = body.NextCursor;

            if (cursor is null)
            {
                break;
            }
        }

        // 25 строк по 10 на страницу: 10 + 10 + 5, третья страница закрывает список.
        Assert.Equal(3, (collected.Count + 9) / 10);
        Assert.Equal(expected, collected);
        Assert.Equal(expected.Count, collected.Distinct().Count());
    }

    [Fact(Timeout = 180_000)]
    public async Task Entity_type_filter_returns_only_that_type()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-type-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var at = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var userId = await data.SeedAuditAsync(
            at.AddMinutes(1), admin.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var barberId = await data.SeedAuditAsync(
            at.AddMinutes(2), admin.Id, "barber_profile.updated", "barber_profile", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var reviewId = await data.SeedAuditAsync(
            at.AddMinutes(3), admin.Id, "review.updated", "review", Guid.NewGuid(), IS_ACTIVE_CHANGE);

        await app.AuthenticateAsync(admin.Email);

        using (var users = await app.Client.GetAsync($"{ENDPOINT}?entityType=user"))
        {
            var page = await ReadPageAsync(users);
            var row = Assert.Single(page.Items);
            Assert.Equal(userId, row.Id);
            Assert.Equal("user", row.EntityType);
        }

        using (var all = await app.Client.GetAsync($"{ENDPOINT}?entityType=all"))
        {
            var page = await ReadPageAsync(all);
            Assert.Equal(3, page.Items.Count);
            Assert.Contains(page.Items, item => item.Id == userId);
            Assert.Contains(page.Items, item => item.Id == barberId);
            Assert.Contains(page.Items, item => item.Id == reviewId);
        }

        foreach (var name in new[] { "Review", "REVIEW" })
        {
            using var reviews = await app.Client.GetAsync($"{ENDPOINT}?entityType={name}");
            var page = await ReadPageAsync(reviews);
            var row = Assert.Single(page.Items);
            Assert.Equal(reviewId, row.Id);
            Assert.Equal("review", row.EntityType);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Unknown_entity_type_is_rejected_with_400()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"audit-type-bad-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var value in new[] { "wrong", "up", "0" })
        {
            using var response = await app.Client.GetAsync($"{ENDPOINT}?entityType={value}");
            await AssertBadRequestAsync(response, "admin.audit.entityType");
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Actor_filter_matches_email_and_escapes_wildcards()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-actor-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var actor = await app.CreateUserAsync(
            $"e2e-actor-{Guid.NewGuid():N}@barbersearch.test");
        var other = await app.CreateUserAsync(
            $"plain-actor-{Guid.NewGuid():N}@barbersearch.test");

        var at = new DateTimeOffset(2026, 9, 10, 11, 0, 0, TimeSpan.Zero);
        var actorRow = await data.SeedAuditAsync(
            at.AddMinutes(1), actor.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var otherRow = await data.SeedAuditAsync(
            at.AddMinutes(2), other.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);

        await app.AuthenticateAsync(admin.Email);

        foreach (var search in new[] { "e2e-", "E2E-" })
        {
            using var response = await app.Client.GetAsync(
                $"{ENDPOINT}?actor={Uri.EscapeDataString(search)}");
            var page = await ReadPageAsync(response);

            var row = Assert.Single(page.Items);
            Assert.Equal(actorRow, row.Id);
            Assert.Equal(actor.Email, row.Actor?.Email);
        }

        // «%» и «_» — литералы, а не шаблон ILIKE: в адресах их нет, значит совпадений нет.
        foreach (var search in new[] { "%", "_" })
        {
            using var response = await app.Client.GetAsync(
                $"{ENDPOINT}?actor={Uri.EscapeDataString(search)}");
            var page = await ReadPageAsync(response);

            Assert.DoesNotContain(page.Items, item => item.Id == otherRow);
            Assert.Empty(page.Items);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Actor_may_be_deleted_and_the_row_stays()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-ghost-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var deletedActorId = Guid.NewGuid();

        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var rowId = await data.SeedAuditAsync(
            at, deletedActorId, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);

        await app.AuthenticateAsync(admin.Email);

        using (var all = await app.Client.GetAsync(ENDPOINT))
        {
            var page = await ReadPageAsync(all);
            var row = Assert.Single(page.Items);

            Assert.Equal(rowId, row.Id);
            Assert.NotNull(row.Actor);
            Assert.Equal(deletedActorId, row.Actor.Id);
            Assert.Null(row.Actor.Email);
        }

        using var filtered = await app.Client.GetAsync($"{ENDPOINT}?actor=ghost");
        var filteredPage = await ReadPageAsync(filtered);
        Assert.Empty(filteredPage.Items);
    }

    [Fact(Timeout = 120_000)]
    public async Task Rows_without_actor_are_returned_as_system()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-system-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var rowId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            null,
            "user.updated",
            "user",
            Guid.NewGuid(),
            IS_ACTIVE_CHANGE);

        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);
        var page = await ReadPageAsync(response);

        var row = Assert.Single(page.Items);
        Assert.Equal(rowId, row.Id);
        Assert.Null(row.Actor);
    }

    [Fact(Timeout = 180_000)]
    public async Task Period_filter_includes_both_utc_days()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-period-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var beforeId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 9, 23, 59, 59, TimeSpan.Zero),
            admin.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var startOfDayId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            admin.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var endOfDayId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 10, 23, 59, 59, TimeSpan.Zero),
            admin.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);
        var afterId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
            admin.Id, "user.updated", "user", Guid.NewGuid(), IS_ACTIVE_CHANGE);

        await app.AuthenticateAsync(admin.Email);

        using (var response = await app.Client.GetAsync(
                   $"{ENDPOINT}?from=2026-09-10&to=2026-09-10"))
        {
            var page = await ReadPageAsync(response);
            var ids = page.Items.Select(item => item.Id).ToList();

            Assert.Equal(2, ids.Count);
            Assert.Contains(startOfDayId, ids);
            Assert.Contains(endOfDayId, ids);
            Assert.DoesNotContain(beforeId, ids);
            Assert.DoesNotContain(afterId, ids);
        }

        using var inverted = await app.Client.GetAsync(
            $"{ENDPOINT}?from=2026-09-11&to=2026-09-10");
        await AssertBadRequestAsync(inverted, "admin.audit.period");

        using var impossible = await app.Client.GetAsync($"{ENDPOINT}?from=2026-13-40");
        await AssertBadRequestAsync(impossible, "admin.audit.period");
    }

    [Fact(Timeout = 180_000)]
    public async Task Payload_changes_are_parsed_into_ordered_field_changes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-payload-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var roleRowId = await data.SeedAuditAsync(
            at.AddMinutes(1),
            admin.Id,
            "user.updated",
            "user",
            Guid.NewGuid(),
            """
            {"changes":{"role":{"from":"Customer","to":"Admin"},"isActive":{"from":true,"to":false}}}
            """);
        var secretRowId = await data.SeedAuditAsync(
            at.AddMinutes(2),
            admin.Id,
            "user.updated",
            "user",
            Guid.NewGuid(),
            """
            {"changes":{"passwordHash":{"from":"[redacted]","to":"[redacted]"}}}
            """);

        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);
        var page = await ReadPageAsync(response);

        var roleRow = Assert.Single(page.Items, item => item.Id == roleRowId);
        Assert.Equal(2, roleRow.Changes.Count);
        Assert.Equal(["isActive", "role"], roleRow.Changes.Select(change => change.Field));

        var isActive = roleRow.Changes[0];
        Assert.True(isActive.From?.GetBoolean());
        Assert.False(isActive.To?.GetBoolean());

        var role = roleRow.Changes[1];
        Assert.Equal("Customer", role.From?.GetString());
        Assert.Equal("Admin", role.To?.GetString());

        var secretRow = Assert.Single(page.Items, item => item.Id == secretRowId);
        var secret = Assert.Single(secretRow.Changes);
        Assert.Equal("[redacted]", secret.From?.GetString());
        Assert.Equal("[redacted]", secret.To?.GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Payload_values_become_created_changes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-values-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var rowId = await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            admin.Id,
            "user.created",
            "user",
            Guid.NewGuid(),
            """
            {"values":{"email":"created-audit@barbersearch.test","role":"Customer","isActive":true}}
            """);

        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);
        var page = await ReadPageAsync(response);

        var row = Assert.Single(page.Items);
        Assert.Equal(rowId, row.Id);
        Assert.Equal(3, row.Changes.Count);
        Assert.Equal(["email", "isActive", "role"], row.Changes.Select(change => change.Field));

        foreach (var change in row.Changes)
        {
            Assert.Null(change.From);
        }

        Assert.Equal("created-audit@barbersearch.test", row.Changes[0].To?.GetString());
        Assert.True(row.Changes[1].To?.GetBoolean());
        Assert.Equal("Customer", row.Changes[2].To?.GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Unexpected_payload_shape_does_not_break_the_list()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-shape-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var objectRowId = await data.SeedAuditAsync(
            at.AddMinutes(1), admin.Id, "user.updated", "user", Guid.NewGuid(), """{"foo":1}""");
        var arrayRowId = await data.SeedAuditAsync(
            at.AddMinutes(2), admin.Id, "user.updated", "user", Guid.NewGuid(), "[]");

        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);
        var page = await ReadPageAsync(response);

        Assert.Equal(2, page.Items.Count);

        foreach (var id in new[] { objectRowId, arrayRowId })
        {
            var row = Assert.Single(page.Items, item => item.Id == id);
            Assert.Empty(row.Changes);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Page_size_out_of_range_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"audit-size-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var value in new[] { 0, 101 })
        {
            using var response = await app.Client.GetAsync($"{ENDPOINT}?pageSize={value}");
            await AssertBadRequestAsync(response, "admin.audit.pageSize");
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Broken_cursor_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"audit-cursor-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.CreateUserAsync($"audit-cursor-extra-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(admin.Email);

        using (var garbage = await app.Client.GetAsync($"{ENDPOINT}?cursor=garbage"))
        {
            await AssertBadRequestAsync(garbage, "admin.audit.cursor.invalid");
        }

        // Чужой курсор: формат общий, но область (список) в нём своя.
        using var users = await app.Client.GetAsync("api/admin/users?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);

        var usersBody = await users.Content.ReadAsStringAsync();
        using var usersDocument = JsonDocument.Parse(usersBody);
        var foreignCursor = usersDocument.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(foreignCursor));

        using var foreign = await app.Client.GetAsync(
            $"{ENDPOINT}?cursor={Uri.EscapeDataString(foreignCursor!)}");
        await AssertBadRequestAsync(foreign, "admin.audit.cursor.invalid");
    }

    [Fact(Timeout = 180_000)]
    public async Task Catalogue_and_salon_edits_are_not_audited()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var admin = await app.CreateUserAsync(
            $"audit-not-audited-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        // Одна настоящая строка аудита, чтобы «ничего не изменилось» было видно на числе > 0.
        await data.SeedAuditAsync(
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            admin.Id,
            "user.updated",
            "user",
            Guid.NewGuid(),
            IS_ACTIVE_CHANGE);

        var before = await data.CountAuditRowsAsync();

        await app.AuthenticateAsync(admin.Email);

        Guid hairServiceId;

        using (var created = await app.Client.PostAsync(
                   "api/hair-services",
                   JsonContent.Create(
                       new CreateHairServiceRequest(
                           $"Not Audited {Guid.NewGuid():N}",
                           ServiceCategory.Beard,
                           30,
                           null),
                       options: JsonOptions)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            hairServiceId = body.Id;
        }

        using (var deactivated = await app.Client.DeleteAsync(
                   $"api/hair-services/{hairServiceId}/activation"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        }

        Guid salonId;

        using (var created = await app.Client.PostAsync(
                   "api/salons",
                   JsonContent.Create(
                       new CreateSalonRequest(
                           $"Not Audited Salon {Guid.NewGuid():N}",
                           "ul. Testovaya, 1",
                           "Kazan",
                           "Russia",
                           "Europe/Moscow",
                           55.7963,
                           49.1088,
                           null,
                           null,
                           null),
                       options: JsonOptions)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            salonId = body.Id;
        }

        using (var moved = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salonId}/location",
                   new UpdateSalonLocationRequest(
                       "ul. Novaya, 2",
                       "Kazan",
                       "Russia",
                       "Europe/Moscow",
                       55.7900,
                       49.1200,
                       null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        Assert.Equal(before, await data.CountAuditRowsAsync());
    }

    private static async Task<AuditPage> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var items = root.GetProperty("items")
            .EnumerateArray()
            .Select(ReadRow)
            .ToList();

        var nextCursor = root.TryGetProperty("nextCursor", out var cursor)
            && cursor.ValueKind != JsonValueKind.Null
                ? cursor.GetString()
                : null;

        return new AuditPage(items, nextCursor);
    }

    private static AuditRow ReadRow(JsonElement element)
    {
        AuditActor? actor = null;

        if (element.GetProperty("actor") is { ValueKind: JsonValueKind.Object } actorElement)
        {
            var email = actorElement.GetProperty("email");

            actor = new AuditActor(
                actorElement.GetProperty("id").GetGuid(),
                email.ValueKind == JsonValueKind.Null ? null : email.GetString());
        }

        var changes = element.GetProperty("changes")
            .EnumerateArray()
            .Select(change => new AuditChange(
                change.GetProperty("field").GetString()!,
                Clone(change.GetProperty("from")),
                Clone(change.GetProperty("to"))))
            .ToList();

        return new AuditRow(
            element.GetProperty("id").GetGuid(),
            element.GetProperty("occurredAt").GetDateTimeOffset(),
            element.GetProperty("action").GetString()!,
            element.GetProperty("entityType").GetString()!,
            element.GetProperty("entityId").GetGuid(),
            actor,
            changes);
    }

    private static JsonElement? Clone(JsonElement element)
        => element.ValueKind == JsonValueKind.Null ? null : element.Clone();

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, body, StringComparison.Ordinal);
    }

    private sealed record AuditSeed(Guid Id, DateTimeOffset OccurredAt);

    private sealed record AuditPage(IReadOnlyList<AuditRow> Items, string? NextCursor);

    private sealed record AuditRow(
        Guid Id,
        DateTimeOffset OccurredAt,
        string Action,
        string EntityType,
        Guid EntityId,
        AuditActor? Actor,
        IReadOnlyList<AuditChange> Changes);

    private sealed record AuditActor(Guid Id, string? Email);

    private sealed record AuditChange(string Field, JsonElement? From, JsonElement? To);
}
