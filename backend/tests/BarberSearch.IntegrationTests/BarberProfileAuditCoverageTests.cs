using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.BarberServices.UpdateBarberServicePrice;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Покрытие журнала аудита карточки барбера и прайса (#96): текст «О себе» и «Правила
/// опоздания», цена, длительность, скрытие и возврат услуги, добавление услуги, а также
/// девятый фильтр <c>GET api/admin/audit?entityType=barber_service</c>.
/// </summary>
/// <remarks>
/// <para>
/// Один <c>[Fact]</c> — одно обещание: позитив («строка есть с нужными полями»)
/// и негатив («строки нет») — разные тесты. Действия идут реальными HTTP-запросами,
/// строки читаются из <c>audit_log</c> через <see cref="TestData.ReadAuditRowsAsync"/>,
/// а фильтр проверяется ответом <c>GET api/admin/audit</c> под администратором.
/// </para>
/// <para>
/// Карточка и прайс — два типа журнала: <c>barber_profile</c> (текст) и <c>barber_service</c>
/// (цена, длительность, активность). События <c>barber_service.deleted</c> не существует:
/// <c>DELETE</c> в API деактивирует строку, потому что на неё ссылаются брони.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BarberProfileAuditCoverageTests
{
    private const string ENDPOINT = "api/admin/audit";

    private readonly PostgresFixture _database;

    public BarberProfileAuditCoverageTests(PostgresFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// Правка карточки («О себе» и «Правила опоздания») даёт ровно одну строку
    /// <c>barber_profile.updated</c> от барбера, а имя и списки в diff не попадают.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Profile_card_text_change_writes_one_row_without_display_name()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Card Barber");

        // Снимок «до» для bio: TestData создаёт профиль с bio = null, а обещание — текстовые
        // from/to. Прямой UPDATE сидирует baseline и аудита не пишет: Capture живёт
        // в RepositoryBase.SaveChangesAsync, а не в ApplicationDbContext.
        await SeedBioAsync(app, barber.Profile.Id, "О себе до правки");

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await PutJsonAsync(
                   app.Client,
                   $"api/barber-profiles/{barber.Profile.Id}",
                   new BarberProfileUpdateRequest(
                       DisplayName: "Audit Card Barber Renamed",
                       YearsOfExperience: 5,
                       Bio: "О себе после правки",
                       Languages: [Language.De],
                       CertificatesAndTraining: [],
                       Achievements: ["Новая награда"],
                       PaymentMethods: [],
                       CommunicationFormat: [],
                       Goodies: [],
                       LatePolicy: "Жду максимум 15 минут",
                       AdditionalServices: [])))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "barber_profile"
                && candidate.Action == "barber_profile.updated"
                && candidate.EntityId == barber.Profile.Id);

        Assert.Equal(barber.User.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        // Списки (языки, достижения), специализации и стаж в allowlist #96 не входят (D3):
        // их diff нечитаем и раздувает payload. Поэтому в строке ровно два поля.
        Assert.Equal(2, changes.EnumerateObject().Count());

        var bio = changes.GetProperty("bio");
        Assert.Equal(JsonValueKind.String, bio.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.String, bio.GetProperty("to").ValueKind);
        Assert.Equal("О себе до правки", bio.GetProperty("from").GetString());
        Assert.Equal("О себе после правки", bio.GetProperty("to").GetString());

        var latePolicy = changes.GetProperty("latePolicy");
        Assert.Equal(JsonValueKind.String, latePolicy.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.String, latePolicy.GetProperty("to").ValueKind);
        Assert.Equal(string.Empty, latePolicy.GetProperty("from").GetString());
        Assert.Equal("Жду максимум 15 минут", latePolicy.GetProperty("to").GetString());

        // displayName исключён решением #96 D4: это EF owned-тип, и при замене инстанса
        // в UpdateProfile EF теряет снимок оригинала — from/to построить нечем. Поле
        // фиксируется отсутствием ключа, а не тихим пропуском.
        Assert.False(changes.TryGetProperty("displayName", out _));
        Assert.False(changes.TryGetProperty("languages", out _));
        Assert.False(changes.TryGetProperty("achievements", out _));
        Assert.False(changes.TryGetProperty("specializations", out _));
    }

    /// <summary>
    /// Правка цены через <c>PUT …/services/{hairServiceId}</c> пишет
    /// <c>barber_service.updated</c> с числовым <c>price.from/to</c> и автором-барбером;
    /// неизменившаяся длительность в diff не попадает.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Service_update_writes_price_change_with_numbers_and_barber_actor()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Price Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var url = $"api/barber-profiles/{barber.Profile.Id}/services/{barber.HairService.Id}";

        // Длительность не меняется (30 = 30): строка второй правки обязана нести только цену.
        using (var first = await PutJsonAsync(
                   app.Client,
                   url,
                   new UpdateBarberProfileServiceRequest(1500m, 30)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var second = await PutJsonAsync(
                   app.Client,
                   url,
                   new UpdateBarberProfileServiceRequest(2000m, 30)))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        var updated = (await data.ReadAuditRowsAsync())
            .Where(candidate => candidate.EntityType == "barber_service"
                && candidate.EntityId == barber.Service.Id
                && candidate.Action == "barber_service.updated")
            .ToList();

        Assert.Equal(2, updated.Count);

        var row = Assert.Single(updated, candidate => PriceTo(candidate.Payload) == 2000m);

        Assert.Equal(barber.User.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var price = changes.GetProperty("price");
        Assert.Equal(JsonValueKind.Number, price.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Number, price.GetProperty("to").ValueKind);
        Assert.Equal(1500m, price.GetProperty("from").GetDecimal());
        Assert.Equal(2000m, price.GetProperty("to").GetDecimal());

        Assert.False(changes.TryGetProperty("durationMinutes", out _));
    }

    /// <summary>
    /// Вторая ручка цены (<c>PUT …/services/{barberServiceId}/price</c>) даёт тот же
    /// результат: одна <c>barber_service.updated</c> с числовым <c>price.from/to</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Service_price_endpoint_writes_the_same_price_change()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Price Endpoint Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var url = $"api/barber-profiles/{barber.Profile.Id}/services/{barber.Service.Id}/price";

        using (var first = await PutJsonAsync(
                   app.Client,
                   url,
                   new UpdateBarberServicePriceRequest(1500m)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var second = await PutJsonAsync(
                   app.Client,
                   url,
                   new UpdateBarberServicePriceRequest(2000m)))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        var updated = (await data.ReadAuditRowsAsync())
            .Where(candidate => candidate.EntityType == "barber_service"
                && candidate.EntityId == barber.Service.Id
                && candidate.Action == "barber_service.updated")
            .ToList();

        Assert.Equal(2, updated.Count);

        var row = Assert.Single(updated, candidate => PriceTo(candidate.Payload) == 2000m);

        Assert.Equal(barber.User.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var price = changes.GetProperty("price");
        Assert.Equal(JsonValueKind.Number, price.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Number, price.GetProperty("to").ValueKind);
        Assert.Equal(1500m, price.GetProperty("from").GetDecimal());
        Assert.Equal(2000m, price.GetProperty("to").GetDecimal());
    }

    /// <summary>
    /// <c>DELETE …/services/{hairServiceId}</c> пишет <c>barber_service.updated</c>
    /// с <c>isActive: true → false</c>; события <c>barber_service.deleted</c> нет нигде.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Service_deactivation_writes_is_active_change_and_no_deleted_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Deactivation Barber");

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services/{barber.HairService.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var rows = await data.ReadAuditRowsAsync();

        var row = Assert.Single(
            rows,
            candidate => candidate.EntityType == "barber_service"
                && candidate.EntityId == barber.Service.Id);

        Assert.Equal("barber_service.updated", row.Action);
        Assert.Equal(barber.User.Id, row.ActorUserId);

        using (var payload = JsonDocument.Parse(row.Payload))
        {
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Single(changes.EnumerateObject());

            var isActive = changes.GetProperty("isActive");
            Assert.True(isActive.GetProperty("from").GetBoolean());
            Assert.False(isActive.GetProperty("to").GetBoolean());
        }

        // У типа нет deleted: DELETE в API — деактивация, событие «удалили» не выдумывается.
        Assert.DoesNotContain(rows, candidate => candidate.Action == "barber_service.deleted");
        Assert.DoesNotContain(rows, candidate => candidate.EntityType == "barber_service"
            && candidate.Action.EndsWith(".deleted", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>POST …/services</c> новой услуги пишет <c>barber_service.created</c> с полным
    /// набором <c>values</c>, а повторное добавление активной услуги отвечает <c>409</c>
    /// и не оставляет новых строк.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Service_add_writes_created_row_and_active_duplicate_conflicts_without_new_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Add Service Barber");
        var hairService = await data.CreateHairServiceAsync("Audit Added Service");

        await app.AuthenticateAsync(barber.User.Email);

        var url = $"api/barber-profiles/{barber.Profile.Id}/services";

        using (var response = await TestApp.PostJsonAsync(
                   app.Client,
                   url,
                   new AddBarberProfileServiceRequest(hairService.Id, 1200m, 45)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var created = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "barber_service"
                && candidate.Action == "barber_service.created");

        Assert.NotEqual(Guid.Empty, created.EntityId);
        Assert.NotEqual(barber.Service.Id, created.EntityId);
        Assert.Equal(barber.User.Id, created.ActorUserId);

        using (var payload = JsonDocument.Parse(created.Payload))
        {
            var values = payload.RootElement.GetProperty("values");

            Assert.Equal(
                barber.Profile.Id.ToString(),
                values.GetProperty("barberProfileId").GetString());
            Assert.Equal(hairService.Id.ToString(), values.GetProperty("hairServiceId").GetString());

            var price = values.GetProperty("price");
            Assert.Equal(JsonValueKind.Number, price.ValueKind);
            Assert.Equal(1200m, price.GetDecimal());

            Assert.Equal(45, values.GetProperty("durationMinutes").GetInt32());
            Assert.True(values.GetProperty("isActive").GetBoolean());
        }

        var before = await data.CountAuditRowsAsync("barber_service");

        using (var duplicate = await TestApp.PostJsonAsync(
                   app.Client,
                   url,
                   new AddBarberProfileServiceRequest(hairService.Id, 1300m, 50)))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains(
                "barberProfile.addService.duplicate",
                await duplicate.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.Equal(before, await data.CountAuditRowsAsync("barber_service"));
    }

    /// <summary>
    /// Админский фильтр знает девятый тип: <c>barber_service</c> возвращает строки прайса,
    /// <c>all</c> их включает, а числовое значение типа по-прежнему <c>400</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Barber_service_filter_returns_rows_and_numbers_are_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Filter Barber");

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await PutJsonAsync(
                   app.Client,
                   $"api/barber-profiles/{barber.Profile.Id}/services/{barber.Service.Id}/price",
                   new UpdateBarberServicePriceRequest(2000m)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var admin = await app.CreateUserAsync(
            $"audit-profile-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        var barberServiceRows = await ReadPageAsync(
            app.Client,
            $"{ENDPOINT}?entityType=barber_service");

        Assert.NotEmpty(barberServiceRows);
        Assert.All(barberServiceRows, row => Assert.Equal("barber_service", row.EntityType));

        var row = Assert.Single(
            barberServiceRows,
            candidate => candidate.EntityId == barber.Service.Id);

        Assert.Equal("barber_service.updated", row.Action);
        Assert.NotNull(row.Actor);
        Assert.Equal(barber.User.Id, row.Actor!.Id);

        var allRows = await ReadPageAsync(app.Client, $"{ENDPOINT}?entityType=all");

        Assert.Contains(
            allRows,
            candidate => candidate.EntityType == "barber_service"
                && candidate.EntityId == barber.Service.Id);

        // Разбор строковый: числа не должны приниматься за enum (D6).
        foreach (var value in new[] { "9", "photo" })
        {
            using var response = await app.Client.GetAsync($"{ENDPOINT}?entityType={value}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "admin.audit.entityType",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Барбер читает админский журнал — <c>403</c>: новое покрытие не расширяет доступ.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Barber_reading_the_audit_journal_gets_403()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Access Barber");

        await app.AuthenticateAsync(barber.User.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Сидирует baseline «О себе»: профиль создаётся TestData с <c>bio = null</c>, а тесту
    /// нужен текстовый <c>from</c>. Прямой <c>UPDATE</c> не обходит проверки схемы
    /// (тип колонки и ограничения остаются) и не пишет аудит — как <see cref="TestData"/>.
    /// </summary>
    private static async Task SeedBioAsync(TestApp app, Guid barberProfileId, string bio)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE barber_profiles
            SET bio = {bio}
            WHERE id = {barberProfileId}
            """);

        Assert.Equal(1, updated);
    }

    /// <summary>
    /// PUT с телом, сериализованным как у настоящего клиента: доменные enum'ы — строками,
    /// иначе сервер отвечает <c>400</c> на числовые значения (R-23).
    /// </summary>
    private static Task<HttpResponseMessage> PutJsonAsync(
        HttpClient client,
        string url,
        object body)
        => client.PutAsync(url, JsonContent.Create(body, options: TestApp.JsonOptions));

    /// <summary>
    /// Цена из <c>changes.price.to</c> строки аудита или <c>null</c>, если поля нет.
    /// </summary>
    private static decimal? PriceTo(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return document.RootElement
            .GetProperty("changes")
            .TryGetProperty("price", out var price)
            && price.TryGetProperty("to", out var to)
            && to.ValueKind == JsonValueKind.Number
                ? to.GetDecimal()
                : null;
    }

    private static async Task<IReadOnlyList<AdminAuditEntryResponse>> ReadPageAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminAuditEntryResponse>>(TestApp.JsonOptions);

        Assert.NotNull(page);

        return page.Items;
    }
}
