using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarberSearch.Contracts.Auth.EmailVerification;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Полный сценарий подтверждения email на реальном хосте и реальной PostgreSQL.
/// </summary>
/// <remarks>
/// Регистрация → письмо перехвачено → ссылка ведёт на <c>/verify-email?token=…</c> →
/// confirm <c>204</c> → <c>GET api/users/me</c> сразу видит флаг, <c>GET api/auth/me</c> —
/// только после refresh (клейм в уже выданном токене не переписывается) → админский фильтр
/// «Без подтверждения email» больше не содержит пользователя. Отправка писем подменена
/// <see cref="CapturingEmailSender"/>: настоящий SMTP в тесте не участвует.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class EmailVerificationTests
{
    private const string PublicUrl = "http://localhost:5173";

    private const string Subject =
        "Подтвердите адрес электронной почты — BarberSearch";

    private readonly PostgresFixture _database;

    public EmailVerificationTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Registration_sends_link_confirm_verifies_email_and_updates_admin_filter()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("email-verification");
        var userId = await app.RegisterAsync(email);

        // Регистрация только ставит письмо в outbox; диспетчер отправляет его отдельным тиком.
        await app.RunEmailDispatchAsync();

        var registrationMessage = Assert.Single(sender.Messages);
        Assert.Equal(email, registrationMessage.To);
        Assert.Equal(Subject, registrationMessage.Subject);

        var registrationLink = ExtractVerificationLink(registrationMessage);
        Assert.StartsWith(
            PublicUrl + "/verify-email?token=",
            registrationLink.Link,
            StringComparison.Ordinal);
        Assert.Equal(43, registrationLink.Token.Length);

        var userTokens = await app.LoginAsync(email);
        SetBearer(app.Client, userTokens.AccessToken);

        // Второй request сразу после регистрации — кулдаун 60 секунд.
        using (var cooldown = await app.Client.PostAsync(
                   "api/auth/email-verification",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.Conflict, cooldown.StatusCode);
            Assert.Contains(
                "auth.emailVerification.cooldown",
                await ReadErrorCodesAsync(cooldown));
        }

        // Переотправка после кулдауна отзывает старый токен и присылает новую ссылку.
        clock.SetUtcNow(clock.UtcNow.AddSeconds(61));

        using (var second = await app.Client.PostAsync(
                   "api/auth/email-verification",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        await app.RunEmailDispatchAsync();

        Assert.Equal(2, sender.Messages.Count);

        var message = sender.Messages[^1];
        var link = ExtractVerificationLink(message);
        Assert.NotEqual(registrationLink.Token, link.Token);

        var adminEmail = NewEmail("email-verification-admin");
        await app.CreateUserAsync(adminEmail, UserRole.Admin);
        var adminTokens = await app.LoginAsync(adminEmail);

        // До подтверждения пользователь виден в фильтре «Без подтверждения email».
        SetBearer(app.Client, adminTokens.AccessToken);
        var before = await ReadUnverifiedAsync(app, email);
        Assert.Contains(before.Items, item => item.Id == userId);

        SetBearer(app.Client, userTokens.AccessToken);

        // Ссылку открывают анонимно, в другом браузере: у confirm нет ни cookie, ни токена.
        // Отдельный клиент без Authorization нужен и для честного аудита (actorUserId = null).
        using var anonymous = app.CreateClient();

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/email-verification/confirm",
                   new ConfirmEmailVerificationRequest(link.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        // Кабинет читает флаг из базы — он виден сразу.
        var profile = await app.Client.GetFromJsonAsync<MyProfileResponse>(
            "api/users/me",
            TestApp.JsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(userId, profile.Id);
        Assert.True(profile.IsEmailVerified);

        // /auth/me читает claims: в токене, выданном до подтверждения, флага ещё нет.
        var claimsBeforeRefresh = await app.Client.GetFromJsonAsync<MeResponse>(
            "api/auth/me",
            TestApp.JsonOptions);
        Assert.NotNull(claimsBeforeRefresh);
        Assert.False(claimsBeforeRefresh.IsEmailVerified);

        // После подтверждения админский фильтр «Без подтверждения email» пуст.
        SetBearer(app.Client, adminTokens.AccessToken);
        var after = await ReadUnverifiedAsync(app, email);
        Assert.DoesNotContain(after.Items, item => item.Id == userId);

        SetBearer(app.Client, userTokens.AccessToken);

        // Refresh перевыпускает access и клейм email_verified появляется.
        using (var refresh = await app.Client.PostAsJsonAsync(
                   "api/auth/refresh",
                   new RefreshTokenRequest(userTokens.RefreshToken)))
        {
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

            var refreshed = await refresh.Content.ReadFromJsonAsync<TokenResponse>(
                TestApp.JsonOptions);
            Assert.NotNull(refreshed);

            SetBearer(app.Client, refreshed.AccessToken);
        }

        var claimsAfterRefresh = await app.Client.GetFromJsonAsync<MeResponse>(
            "api/auth/me",
            TestApp.JsonOptions);
        Assert.NotNull(claimsAfterRefresh);
        Assert.True(claimsAfterRefresh.IsEmailVerified);

        // Повторный клик по ссылке идемпотентен.
        using (var repeated = await anonymous.PostAsJsonAsync(
                   "api/auth/email-verification/confirm",
                   new ConfirmEmailVerificationRequest(link.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }

        // Битый токен — 400 auth.emailVerification.invalid, а не 500.
        using (var garbage = await anonymous.PostAsJsonAsync(
                   "api/auth/email-verification/confirm",
                   new ConfirmEmailVerificationRequest("not a valid token")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
            Assert.Contains(
                "auth.emailVerification.invalid",
                await ReadErrorCodesAsync(garbage));
        }

        // Аудит подтверждения: actorUserId = null, изменение isEmailVerified.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var auditRows = await db.AuditLogs
            .AsNoTracking()
            .Where(row => row.EntityId == userId && row.Action == "user.updated")
            .ToListAsync();

        var audit = Assert.Single(auditRows);
        Assert.Null(audit.ActorUserId);
        Assert.Equal("user", audit.EntityType);
        Assert.Contains("isEmailVerified", audit.Payload, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Request_without_configured_email_returns_unavailable_but_registration_works()
    {
        // Подменный отправитель тоже «не настроен»: готовность почты — его свойство,
        // а не глобальной конфигурации, которую видит только реальный SMTP-транспорт.
        var sender = new CapturingEmailSender { IsAvailable = false };

        await using var app = await TestApp.StartAsync(
            _database,
            new Dictionary<string, string?>
            {
                ["Email__Enabled"] = "false",
                ["Email__Host"] = string.Empty,
                ["Email__FromAddress"] = string.Empty,
                ["App__PublicUrl"] = PublicUrl
            },
            emailSender: sender);

        var email = NewEmail("email-unconfigured");
        var userId = await app.RegisterAsync(email);

        // Регистрация не зависит от почты: пользователь создан, письма нет.
        Assert.Empty(sender.Messages);

        var tokens = await app.LoginAsync(email);
        SetBearer(app.Client, tokens.AccessToken);

        using var response = await app.Client.PostAsync(
            "api/auth/email-verification",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "auth.emailVerification.unavailable",
            await ReadErrorCodesAsync(response));

        // 409 приходит до записи в outbox: строк за запрос не появилось.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.EmailOutbox.CountAsync());
        }

        // Пользователь существует и не подтверждён: отказ почты не сломал регистрацию.
        var profile = await app.Client.GetFromJsonAsync<MyProfileResponse>(
            "api/users/me",
            TestApp.JsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(userId, profile.Id);
        Assert.False(profile.IsEmailVerified);
    }

    private static Dictionary<string, string?> ConfiguredEmail()
        => new()
        {
            ["Email__Enabled"] = "true",
            ["Email__Host"] = "localhost",
            ["Email__Port"] = "1025",
            ["Email__UseTls"] = "false",
            ["Email__FromAddress"] = "no-reply@barbersearch.test",
            ["App__PublicUrl"] = PublicUrl
        };

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

    private static void SetBearer(HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private static (string Link, string Token) ExtractVerificationLink(
        CapturedEmailMessage message)
    {
        var match = Regex.Match(
            message.HtmlBody,
            @"href=""(?<link>http[^""]*/verify-email\?token=(?<token>[A-Za-z0-9_-]+))""");

        Assert.True(match.Success, message.HtmlBody);

        return (match.Groups["link"].Value, match.Groups["token"].Value);
    }

    private static async Task<PagedResponse<AdminUserResponse>> ReadUnverifiedAsync(
        TestApp app,
        string email)
    {
        var url = $"api/admin/users?status=unverified&search={Uri.EscapeDataString(email)}";
        var page = await app.Client.GetFromJsonAsync<PagedResponse<AdminUserResponse>>(
            url,
            TestApp.JsonOptions);

        Assert.NotNull(page);

        return page;
    }

    private static async Task<IReadOnlyList<string>> ReadErrorCodesAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!document.RootElement.TryGetProperty("errorCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Response {response.StatusCode} does not contain errorCodes.");
        }

        return codes.EnumerateArray()
            .Select(code => code.GetString() ?? string.Empty)
            .ToList();
    }
}
