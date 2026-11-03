using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Подтверждаемое согласие на обработку персональных данных (#42) на реальной PostgreSQL:
/// сервер принимает редакцию Политики, сам ставит UTC-время и IP, хранит запись в <c>users</c>
/// и отдаёт её в админской карточке без IP.
/// </summary>
/// <remarks>
/// Литерал редакции продублирован здесь намеренно: это guard от расхождения с
/// <c>PRIVACY_POLICY_VERSION</c> на клиенте и с <c>PrivacyPolicy.Version</c> в Application.
/// Значения согласия читаются сырым SQL: проверяется физическая колонка, а не проекция домена.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RegistrationConsentTests
{
    /// <summary>
    /// Текущая редакция Политики на дату этапа.
    /// </summary>
    private const string CurrentPolicyVersion = "2026-11-03";

    /// <summary>
    /// Код отказа «согласие не передано» — литерал из публичного контракта.
    /// </summary>
    private const string ConsentRequiredCode = "auth.register.consentRequired";

    /// <summary>
    /// Код отказа «редакция Политики устарела» — литерал из публичного контракта.
    /// </summary>
    private const string ConsentVersionCode = "auth.register.consentVersion";

    private readonly PostgresFixture _database;

    public RegistrationConsentTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_with_current_consent_version_stores_policy_version_time_and_ip()
    {
        await using var app = await TestApp.StartAsync(_database);
        var before = DateTimeOffset.UtcNow;

        using var response = await RegisterAsync(
            app,
            NewEmail("consent-stored"),
            CurrentPolicyVersion);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = DateTimeOffset.UtcNow;

        var created = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(created);

        var consent = Assert.IsType<ConsentRow>(await ReadConsentAsync(app, created.Id));
        Assert.Equal(CurrentPolicyVersion, consent.PolicyVersion);
        Assert.NotNull(consent.ConsentedAt);
        Assert.InRange(consent.ConsentedAt!.Value, before.AddMinutes(-1), after.AddMinutes(1));
        Assert.Equal(TimeSpan.Zero, consent.ConsentedAt.Value.Offset);

        // TestServer может не заполнять RemoteIpAddress: IP проверяется только когда он есть.
        if (consent.IpAddress is not null)
        {
            Assert.True(
                consent.IpAddress.AddressFamily is AddressFamily.InterNetwork
                    or AddressFamily.InterNetworkV6,
                $"Unexpected address family: {consent.IpAddress.AddressFamily}");
        }

        // Журнал — история изменений, а не реестр согласий: полей согласия и IP в payload нет.
        var payload = await ReadUserCreatedPayloadAsync(app, created.Id);
        using var payloadDocument = JsonDocument.Parse(payload);
        Assert.False(HasConsentProperty(payloadDocument.RootElement), payload);

        if (consent.IpAddress is not null)
        {
            Assert.DoesNotContain(consent.IpAddress.ToString(), payload, StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_without_consent_version_does_not_create_user()
    {
        await using var app = await TestApp.StartAsync(_database);
        var usersBefore = await CountUsersAsync(app);

        using var response = await RegisterAsync(
            app,
            NewEmail("consent-missing"),
            consentVersion: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            ConsentRequiredCode,
            await ReadErrorCodesAsync(response),
            StringComparer.Ordinal);
        Assert.Equal(usersBefore, await CountUsersAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_with_outdated_consent_version_does_not_create_user()
    {
        await using var app = await TestApp.StartAsync(_database);
        var usersBefore = await CountUsersAsync(app);

        using var response = await RegisterAsync(
            app,
            NewEmail("consent-outdated"),
            "2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            ConsentVersionCode,
            await ReadErrorCodesAsync(response),
            StringComparer.Ordinal);
        Assert.Equal(usersBefore, await CountUsersAsync(app));
    }

    [Fact(Timeout = 180_000)]
    public async Task Register_accepts_consent_version_with_surrounding_spaces()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var response = await RegisterAsync(
            app,
            NewEmail("consent-padded"),
            $"  {CurrentPolicyVersion}  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(created);

        var consent = Assert.IsType<ConsentRow>(await ReadConsentAsync(app, created.Id));
        Assert.Equal(CurrentPolicyVersion, consent.PolicyVersion);
    }

    [Fact(Timeout = 180_000)]
    public async Task Repeated_registration_of_the_same_email_keeps_the_first_consent_record()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = NewEmail("consent-duplicate");

        using var first = await RegisterAsync(app, email, CurrentPolicyVersion);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var created = await first.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(created);

        var stored = Assert.IsType<ConsentRow>(await ReadConsentAsync(app, created.Id));

        using var second = await RegisterAsync(app, email, CurrentPolicyVersion);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(
            "user.create.emailTaken",
            await ReadErrorCodesAsync(second),
            StringComparer.Ordinal);

        Assert.Equal(1, await CountUsersAsync(app));

        var afterSecond = Assert.IsType<ConsentRow>(await ReadConsentAsync(app, created.Id));
        Assert.Equal(stored, afterSecond);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_card_exposes_consent_version_and_time_without_ip()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(NewEmail("admin-consent"), UserRole.Admin);

        using var registration = await RegisterAsync(
            app,
            NewEmail("consent-card"),
            CurrentPolicyVersion);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var created = await registration.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(created);

        var stored = Assert.IsType<ConsentRow>(await ReadConsentAsync(app, created.Id));

        await app.AuthenticateAsync(admin.Email);

        using var card = await app.Client.GetAsync($"api/admin/users/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);

        var json = await card.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(CurrentPolicyVersion, root.GetProperty("consentPolicyVersion").GetString());
        Assert.Equal(stored.ConsentedAt, root.GetProperty("consentedAt").GetDateTimeOffset());

        // IP — персональные данные: в API он не отдаётся ни полем, ни текстом.
        Assert.False(root.TryGetProperty("consentIp", out _), json);

        if (stored.IpAddress is not null)
        {
            Assert.DoesNotContain(stored.IpAddress.ToString(), json, StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_card_of_a_seeded_user_has_no_consent_record()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(NewEmail("admin-legacy"), UserRole.Admin);
        var legacy = await app.CreateUserAsync(NewEmail("legacy-consent"));

        await app.AuthenticateAsync(admin.Email);

        using var card = await app.Client.GetAsync($"api/admin/users/{legacy.Id}");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);

        var json = await card.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("consentPolicyVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("consentedAt").ValueKind);
        Assert.False(root.TryGetProperty("consentIp", out _), json);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_card_is_closed_for_anonymous_customer_and_barber()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymous = await app.Client.GetAsync("api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customerEmail = NewEmail("consent-customer");
        var customerId = await app.RegisterAsync(customerEmail);
        await app.AuthenticateAsync(customerEmail);

        using var customer = await app.Client.GetAsync($"api/admin/users/{customerId}");
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);

        var barber = await app.CreateUserAsync(NewEmail("consent-barber"), UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using var barberCard = await app.Client.GetAsync($"api/admin/users/{barber.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, barberCard.StatusCode);

        var admin = await app.CreateUserAsync(NewEmail("consent-admin"), UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var adminCard = await app.Client.GetAsync($"api/admin/users/{barber.Id}");
        Assert.Equal(HttpStatusCode.OK, adminCard.StatusCode);
    }

    /// <summary>
    /// Отправляет регистрацию; поле <c>consentVersion</c> не добавляется, когда оно <c>null</c>.
    /// </summary>
    private static Task<HttpResponseMessage> RegisterAsync(
        TestApp app,
        string email,
        string? consentVersion)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = TestApp.Password,
            ["firstName"] = "Integration",
            ["lastName"] = "Test"
        };

        if (consentVersion is not null)
        {
            payload["consentVersion"] = consentVersion;
        }

        return app.Client.PostAsJsonAsync("api/auth/register", payload);
    }

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

    private static async Task<int> CountUsersAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users.CountAsync();
    }

    /// <summary>
    /// Читает три колонки согласия сырым SQL: так проверяется физическая схема, а не проекция.
    /// </summary>
    private static async Task<ConsentRow?> ReadConsentAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT consent_policy_version, consented_at, consent_ip FROM users WHERE id = @id";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ConsentRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<IPAddress>(2));
    }

    private static async Task<string> ReadUserCreatedPayloadAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT payload FROM audit_log WHERE entity_id = @id AND action = 'user.created'";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = userId;
        command.Parameters.Add(parameter);

        var payload = await command.ExecuteScalarAsync();

        return payload as string ?? string.Empty;
    }

    private static async Task<IReadOnlyList<string>> ReadErrorCodesAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("errorCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. codes.EnumerateArray()
            .Where(code => code.ValueKind == JsonValueKind.String)
            .Select(code => code.GetString()!)];
    }

    /// <summary>
    /// Ищет в payload аудита свойство, имя которого упоминает согласие.
    /// </summary>
    /// <remarks>
    /// Проверяется именно имя свойства, а не текст: email тестового пользователя само
    /// содержит слово «consent» и иначе давало бы ложное срабатывание.
    /// </remarks>
    private static bool HasConsentProperty(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Contains("consent", StringComparison.OrdinalIgnoreCase) ||
                        HasConsentProperty(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(HasConsentProperty);
            default:
                return false;
        }
    }

    /// <summary>
    /// Запись согласия, прочитанная прямо из <c>users</c>.
    /// </summary>
    private sealed record ConsentRow(
        string? PolicyVersion,
        DateTimeOffset? ConsentedAt,
        IPAddress? IpAddress);
}
