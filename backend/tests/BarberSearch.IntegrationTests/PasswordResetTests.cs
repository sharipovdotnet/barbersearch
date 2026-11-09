using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarberSearch.Application.PasswordResets;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Auth.PasswordReset;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain;
using BarberSearch.Domain.PasswordResets;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Полный сценарий самостоятельного сброса пароля на реальном хосте и реальной PostgreSQL.
/// </summary>
/// <remarks>
/// Пользователь создаётся напрямую через <see cref="TestApp.CreateUserAsync"/> — без письма
/// подтверждения email, как в жизни у старого аккаунта. Отправка писем подменена
/// <see cref="CapturingEmailSender"/>; SMTP в тестах не участвует.
/// <para>
/// Проверяются именно необратимые последствия сброса: старый пароль и старые сессии мертвы,
/// одноразовая ссылка больше не срабатывает, а адрес, который не существует или деактивирован
/// администратором, не получает письмо и не восстанавливается.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PasswordResetTests
{
    private const string PublicUrl = "http://localhost:5173";

    private const string NewPassword = "NewIntegration!Passw0rd";

    private const string Subject = "Сброс пароля — BarberSearch";

    private readonly PostgresFixture _database;

    public PasswordResetTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Request_sends_link_to_reset_password_page_with_43_character_token()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("reset-request");
        await app.CreateUserAsync(email);

        // Verify-письма не было: пользователь создан в обход регистрации.
        Assert.Empty(sender.Messages);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var message = Assert.Single(sender.Messages);
        Assert.Equal(email, message.To);
        Assert.Equal(Subject, message.Subject);

        var link = ExtractResetLink(message);
        Assert.StartsWith(
            PublicUrl + "/reset-password?token=",
            link.Link,
            StringComparison.Ordinal);
        Assert.Equal(PasswordResetTokenGenerator.TOKEN_LENGTH, link.Token.Length);

        // Ссылка есть и в текстовом дубле, а открытый токен — только внутри ссылки.
        Assert.Contains(link.Link, message.TextBody, StringComparison.Ordinal);
        Assert.Contains("30 минут", message.TextBody, StringComparison.Ordinal);
    }

    [Fact(Timeout = 240_000)]
    public async Task Confirm_changes_password_so_new_login_succeeds_and_old_password_fails()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-confirm");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        using (var newLogin = await app.Client.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(email, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        }

        using (var oldLogin = await app.Client.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(email, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
            Assert.Contains(
                "auth.login.invalidCredentials",
                await ReadErrorCodesAsync(oldLogin));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Confirm_revokes_old_access_token_cookie_and_refresh_token()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-stamp");
        await app.CreateUserAsync(email);

        var tokens = await app.LoginAsync(email);
        await using var session = await app.SignInWithCookieAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        // Security stamp ротирован: уже выданный access-токен мёртв на следующем запросе.
        using (var stale = new HttpRequestMessage(HttpMethod.Get, "api/users/me"))
        {
            stale.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                tokens.AccessToken);

            using var staleResponse = await app.Client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);
        }

        // Cookie-сессия тоже больше не проходит проверку отпечатка.
        using (var staleCookie = await session.Client.GetAsync("api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleCookie.StatusCode);
        }

        // Refresh-токены отозваны все: старый не обменивается на новую пару.
        using (var refresh = await anonymous.PostAsJsonAsync(
                   "api/auth/refresh",
                   new RefreshTokenRequest(tokens.RefreshToken)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Contains("auth.refresh.revoked", await ReadErrorCodesAsync(refresh));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Second_confirm_with_same_token_returns_invalid()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-replay");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        const string firstPassword = "FirstIntegration!Passw0rd";

        using (var first = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, firstPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var repeated = await anonymous.PostAsJsonAsync(
            "api/auth/password-reset/confirm",
            new ConfirmPasswordResetRequest(link.Token, NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Contains("auth.passwordReset.invalid", await ReadErrorCodesAsync(repeated));

        // Второй клик не «доиграл» первый: действует пароль первого подтверждения.
        using (var login = await app.Client.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(email, firstPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Unknown_and_inactive_accounts_get_no_message_and_inactive_cannot_confirm()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        using var anonymous = app.CreateClient();

        // Неизвестный адрес: тот же 204, что и для существующего, но письма нет.
        using (var unknown = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(NewEmail("reset-unknown"))))
        {
            Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        }

        Assert.Empty(sender.Messages);

        // Деактивированный администратором аккаунт тоже не получает письмо.
        var inactiveEmail = NewEmail("reset-inactive");
        var blocked = await new TestData(app).CreateBlockedUserAsync(inactiveEmail);

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(inactiveEmail)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        Assert.Empty(sender.Messages);

        // Токен для неактивного выдаём напрямую в БД: request его не выпускает.
        var clock = new MutableTimeProvider();
        var rawToken = PasswordResetTokenGenerator.Generate();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedUser = await db.Users.SingleAsync(user => user.Id == blocked.Id);
            var originalHash = storedUser.PasswordHash;

            var created = PasswordResetToken.Create(
                blocked.Id,
                PasswordResetTokenGenerator.ComputeHash(rawToken),
                clock.UtcNow,
                clock.UtcNow + PasswordResetLifetime.TOKEN_LIFETIME,
                clock.UtcNow);

            Assert.True(created.IsSuccess);
            db.PasswordResetTokens.Add(created.Value);
            await db.SaveChangesAsync();

            using (var confirm = await anonymous.PostAsJsonAsync(
                       "api/auth/password-reset/confirm",
                       new ConfirmPasswordResetRequest(rawToken, NewPassword)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
                Assert.Contains(
                    "auth.passwordReset.invalid",
                    await ReadErrorCodesAsync(confirm));
            }

            // Сброс не реактивировал аккаунт и не тронул пароль.
            var after = await db.Users
                .AsNoTracking()
                .SingleAsync(user => user.Id == blocked.Id);

            Assert.False(after.IsActive);
            Assert.Equal(originalHash, after.PasswordHash);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Request_without_configured_email_returns_unavailable()
    {
        var sender = new CapturingEmailSender();

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

        var email = NewEmail("reset-unconfigured");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using var response = await anonymous.PostAsJsonAsync(
            "api/auth/password-reset",
            new RequestPasswordResetRequest(email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "auth.passwordReset.unavailable",
            await ReadErrorCodesAsync(response));
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 240_000)]
    public async Task Resend_within_cooldown_is_silent_and_replaces_token_after_61_seconds()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("reset-cooldown");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var first = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        var firstLink = ExtractResetLink(Assert.Single(sender.Messages));

        // Повтор внутри 60 секунд — тихий 204 без письма и без отзыва токена.
        using (var cooldown = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cooldown.StatusCode);
        }

        Assert.Single(sender.Messages);

        clock.SetUtcNow(clock.UtcNow.AddSeconds(61));

        using (var second = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        Assert.Equal(2, sender.Messages.Count);

        var secondLink = ExtractResetLink(sender.Messages[^1]);
        Assert.NotEqual(firstLink.Token, secondLink.Token);

        // Переотправка отозвала прежний токен.
        using (var stale = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(firstLink.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
            Assert.Contains("auth.passwordReset.invalid", await ReadErrorCodesAsync(stale));
        }

        // А новый токен из второго письма работает.
        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(secondLink.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Weak_password_is_rejected_without_consuming_the_token()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-policy");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var weak = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, "short")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

            // Конкретный код зависит от порядка правил политики (словарь проверяется
            // раньше длины), поэтому проверяем семейство кодов и привязку к полю.
            var codes = await ReadErrorCodesAsync(weak);
            Assert.Contains(
                codes,
                code => code.StartsWith("auth.password.", StringComparison.Ordinal));
            Assert.DoesNotContain("auth.passwordReset.invalid", codes);

            using var problem = JsonDocument.Parse(
                await weak.Content.ReadAsStringAsync());
            Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("newPassword", out _));
        }

        // Токен не потреблён: та же ссылка с валидным паролем срабатывает.
        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Reset_lifts_lockout_so_new_password_can_sign_in()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("reset-lockout");
        var user = await app.CreateUserAsync(email);

        // Доводим счётчик неудач до порога: вход заблокирован на минуту.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);

            for (var attempt = 0; attempt < Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
            {
                Assert.True(stored.RegisterFailedLogin(clock.UtcNow).IsSuccess);
            }

            await db.SaveChangesAsync();
        }

        using (var locked = await app.Client.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(email, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
            Assert.Contains("auth.login.lockedOut", await ReadErrorCodesAsync(locked));
        }

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        // Сброс снял блокировку: новый пароль входит сразу, без ожидания LockoutEnd.
        using (var login = await app.Client.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(email, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Reset_marks_email_verified_and_removes_user_from_unverified_filter()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-verified");
        var user = await app.CreateUserAsync(email);
        var userId = user.Id;

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        // Кабинет читает флаг из базы: после сброса адрес подтверждён.
        var tokens = await app.LoginAsync(email, NewPassword);
        SetBearer(app.Client, tokens.AccessToken);

        var profile = await app.Client.GetFromJsonAsync<MyProfileResponse>(
            "api/users/me",
            TestApp.JsonOptions);

        Assert.NotNull(profile);
        Assert.Equal(userId, profile.Id);
        Assert.True(profile.IsEmailVerified);

        // Админский фильтр «Без подтверждения email» больше не показывает пользователя.
        var admin = await app.CreateUserAsync(NewEmail("reset-admin"), UserRole.Admin);
        var adminTokens = await app.LoginAsync(admin.Email);
        SetBearer(app.Client, adminTokens.AccessToken);

        using var unverified = await app.Client.GetAsync(
            $"api/admin/users?status=unverified&search={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);

        var page = await unverified.Content.ReadFromJsonAsync<PagedResponse<AdminUserResponse>>(
            TestApp.JsonOptions);

        Assert.NotNull(page);
        Assert.DoesNotContain(page.Items, item => item.Id == userId);
    }

    [Fact(Timeout = 240_000)]
    public async Task Reset_writes_one_anonymized_user_updated_audit_row_without_secrets()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var email = NewEmail("reset-audit");
        var user = await app.CreateUserAsync(email);
        var userId = user.Id;

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var link = ExtractResetLink(Assert.Single(sender.Messages));

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset/confirm",
                   new ConfirmPasswordResetRequest(link.Token, NewPassword)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.AuditLogs
            .AsNoTracking()
            .Where(row => row.EntityId == userId && row.Action == "user.updated")
            .ToListAsync();

        var audit = Assert.Single(rows);
        Assert.Null(audit.ActorUserId);
        Assert.Equal("user", audit.EntityType);
        Assert.Contains("isEmailVerified", audit.Payload, StringComparison.Ordinal);
        Assert.Contains("[redacted]", audit.Payload, StringComparison.Ordinal);

        // Ни открытый токен, ни пароль в журнал не попадают.
        Assert.DoesNotContain(link.Token, audit.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, audit.Payload, StringComparison.Ordinal);
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

    private static (string Link, string Token) ExtractResetLink(CapturedEmailMessage message)
    {
        var match = Regex.Match(
            message.HtmlBody,
            @"href=""(?<link>http[^""]*/reset-password\?token=(?<token>[A-Za-z0-9_-]+))""");

        Assert.True(match.Success, message.HtmlBody);

        return (match.Groups["link"].Value, match.Groups["token"].Value);
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
