using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarberSearch.Application.EmailChanges;
using BarberSearch.Contracts.Auth.EmailChange;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailChanges;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Полный сценарий смены адреса электронной почты на реальном хосте и реальной PostgreSQL.
/// </summary>
/// <remarks>
/// Пользователь создаётся напрямую через <see cref="TestApp.CreateUserAsync"/>: регистрация
/// не нужна, а её письмо подтверждения только мешало бы считать строки outbox. Отправка
/// писем подменена <see cref="CapturingEmailSender"/>; SMTP в тестах не участвует.
/// <para>
/// Проверяются именно необратимые последствия подтверждения: старый адрес больше не входит,
/// новый входит с прежним паролем, старая cookie и refresh-токен мертвы, повторный клик по
/// ссылке идемпотентен, а в журнале аудита остаётся одна анонимная запись.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class EmailChangeTests
{
    private const string PublicUrl = "http://localhost:5173";

    private const string ConfirmationSubject =
        "Подтвердите новый адрес электронной почты — BarberSearch";

    private const string NoticeSubject =
        "Запрошена смена адреса электронной почты — BarberSearch";

    private readonly PostgresFixture _database;

    public EmailChangeTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Request_queues_two_letters_and_confirm_moves_login_to_new_address()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var oldEmail = NewEmail("email-change");
        var newEmail = NewEmail("email-change-new");
        var user = await app.CreateUserAsync(oldEmail);

        // Браузерная сессия и Bearer-пара выданы до смены адреса: оба должны умереть.
        await using var session = await app.SignInWithCookieAsync(oldEmail);
        var tokens = await app.LoginAsync(oldEmail);
        SetBearer(app.Client, tokens.AccessToken);

        using (var request = await app.Client.PostAsJsonAsync(
                   "api/auth/email-change",
                   new RequestEmailChangeRequest(newEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        // Токен и оба письма зафиксированы одной транзакцией — до диспетчера их две строки.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Equal(1, await db.EmailChangeTokens.CountAsync());
            Assert.Equal(2, await db.EmailOutbox.CountAsync());
        }

        await app.RunEmailDispatchAsync();

        Assert.Equal(2, sender.Messages.Count);

        var confirmation = Assert.Single(
            sender.Messages,
            message => message.To == newEmail && message.Subject == ConfirmationSubject);
        var notice = Assert.Single(
            sender.Messages,
            message => message.To == oldEmail && message.Subject == NoticeSubject);

        var link = ExtractConfirmationLink(confirmation);

        Assert.StartsWith(
            PublicUrl + "/confirm-email-change?token=",
            link.Link,
            StringComparison.Ordinal);
        Assert.Equal(43, link.Token.Length);
        Assert.Contains(link.Link, confirmation.TextBody, StringComparison.Ordinal);

        // Уведомление на старый адрес не содержит ни токена, ни ссылки подтверждения.
        Assert.DoesNotContain(link.Token, notice.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Token, notice.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm-email-change", notice.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", notice.TextBody, StringComparison.Ordinal);

        // Ссылку открывают анонимно, в другом браузере.
        using var anonymous = app.CreateClient();

        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest(link.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        // Security stamp ротирован: старый access-токен мёртв на следующем запросе.
        using (var staleBearer = await app.Client.GetAsync("api/users/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleBearer.StatusCode);
        }

        // Cookie-сессия тоже больше не проходит проверку отпечатка.
        using (var staleCookie = await session.Client.GetAsync("api/auth/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staleCookie.StatusCode);
        }

        // Все refresh-токены отозваны в той же транзакции.
        using (var refresh = await anonymous.PostAsJsonAsync(
                   "api/auth/refresh",
                   new RefreshTokenRequest(tokens.RefreshToken)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Contains("auth.refresh.revoked", await ReadErrorCodesAsync(refresh));
        }

        // Старый адрес для входа больше не работает, новый — работает с прежним паролем.
        using (var oldLogin = await anonymous.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(oldEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
            Assert.Contains(
                "auth.login.invalidCredentials",
                await ReadErrorCodesAsync(oldLogin));
        }

        using (var newLogin = await anonymous.PostAsJsonAsync(
                   "api/auth/login",
                   new LoginRequest(newEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        }

        // Повторный клик по ссылке идемпотентен.
        using (var repeated = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest(link.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }

        await using var dbScope = app.Services.CreateAsyncScope();
        var database = dbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stored = await database.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);

        Assert.Equal(newEmail, stored.Email);
        Assert.True(stored.IsEmailVerified);
        Assert.NotEqual(user.SecurityStamp, stored.SecurityStamp);

        // В базе только хеш: открытый токен существует лишь в письме.
        var storedToken = Assert.Single(await database.EmailChangeTokens
            .AsNoTracking()
            .ToListAsync());
        Assert.Equal(newEmail, storedToken.NewEmail);
        Assert.NotNull(storedToken.ConsumedAt);
        Assert.Null(storedToken.RevokedAt);
        Assert.NotEqual(link.Token, storedToken.TokenHash);
        Assert.Equal(
            EmailChangeTokenGenerator.ComputeHash(link.Token),
            storedToken.TokenHash);

        // Аудит подтверждения: confirm анонимный, поэтому actorUserId = null, а адрес и
        // отпечаток попадают в diff существующего allowlist.
        var auditRows = await database.AuditLogs
            .AsNoTracking()
            .Where(row => row.EntityId == user.Id && row.Action == "user.updated")
            .ToListAsync();

        var audit = Assert.Single(auditRows);
        Assert.Null(audit.ActorUserId);
        Assert.Equal("user", audit.EntityType);
        Assert.Contains("email", audit.Payload, StringComparison.Ordinal);
        Assert.Contains("securityStamp", audit.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Token, audit.Payload, StringComparison.Ordinal);
    }

    [Fact(Timeout = 240_000)]
    public async Task Request_for_address_taken_by_another_account_returns_conflict()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        var firstEmail = NewEmail("email-change-taken-first");
        var secondEmail = NewEmail("email-change-taken-second");
        await app.CreateUserAsync(firstEmail);
        await app.CreateUserAsync(secondEmail);

        var tokens = await app.LoginAsync(firstEmail);
        SetBearer(app.Client, tokens.AccessToken);

        using var response = await app.Client.PostAsJsonAsync(
            "api/auth/email-change",
            new RequestEmailChangeRequest(secondEmail, TestApp.Password));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "user.changeEmail.emailTaken",
            await ReadErrorCodesAsync(response));

        // Отказ приходит до записи: ни токена, ни строки outbox не появилось.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await db.EmailChangeTokens.CountAsync());
        Assert.Equal(0, await db.EmailOutbox.CountAsync());
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 240_000)]
    public async Task Confirm_for_address_taken_after_request_returns_conflict()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var requesterEmail = NewEmail("email-change-race-first");
        var takenEmail = NewEmail("email-change-race-second");
        var requester = await app.CreateUserAsync(requesterEmail);
        await app.CreateUserAsync(takenEmail);

        // Заявка выпущена, когда адрес был свободен, но подтверждают её уже после того,
        // как адрес занял другой аккаунт. Токен кладём напрямую в БД: публичный request
        // такой адрес не пропустит.
        var rawToken = EmailChangeTokenGenerator.Generate();

        await using (var seedScope = app.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var created = EmailChangeToken.Create(
                requester.Id,
                takenEmail,
                EmailChangeTokenGenerator.ComputeHash(rawToken),
                clock.UtcNow,
                clock.UtcNow + EmailChangeLifetime.TOKEN_LIFETIME,
                clock.UtcNow);

            Assert.True(created.IsSuccess);

            db.EmailChangeTokens.Add(created.Value);
            await db.SaveChangesAsync();
        }

        using var anonymous = app.CreateClient();

        using var response = await anonymous.PostAsJsonAsync(
            "api/auth/email-change/confirm",
            new ConfirmEmailChangeRequest(rawToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "user.changeEmail.emailTaken",
            await ReadErrorCodesAsync(response));

        await using var checkScope = app.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await checkDb.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == requester.Id);

        Assert.Equal(requesterEmail, stored.Email);
        Assert.False(stored.IsEmailVerified);
        Assert.Null((await checkDb.EmailChangeTokens.AsNoTracking().SingleAsync()).ConsumedAt);
    }

    [Fact(Timeout = 180_000)]
    public async Task Confirm_with_broken_or_unknown_token_returns_invalid()
    {
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            emailSender: sender);

        using var anonymous = app.CreateClient();

        using (var broken = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest("not a valid token")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
            Assert.Contains(
                "auth.emailChange.invalid",
                await ReadErrorCodesAsync(broken));
        }

        // Синтаксически корректный, но неизвестный токен неотличим от просроченного.
        using (var unknown = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest(EmailChangeTokenGenerator.Generate())))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Contains(
                "auth.emailChange.invalid",
                await ReadErrorCodesAsync(unknown));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Resend_within_cooldown_is_rejected_and_after_60_seconds_revokes_previous_link()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var oldEmail = NewEmail("email-change-cooldown");
        var newEmail = NewEmail("email-change-cooldown-new");
        await app.CreateUserAsync(oldEmail);

        var tokens = await app.LoginAsync(oldEmail);
        SetBearer(app.Client, tokens.AccessToken);

        using (var first = await app.Client.PostAsJsonAsync(
                   "api/auth/email-change",
                   new RequestEmailChangeRequest(newEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        await app.RunEmailDispatchAsync();

        var firstConfirmation = Assert.Single(
            sender.Messages,
            message => message.To == newEmail && message.Subject == ConfirmationSubject);
        var firstLink = ExtractConfirmationLink(firstConfirmation);

        // Повтор внутри 60 секунд — 409, письма и нового токена нет.
        using (var cooldown = await app.Client.PostAsJsonAsync(
                   "api/auth/email-change",
                   new RequestEmailChangeRequest(newEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.Conflict, cooldown.StatusCode);
            Assert.Contains(
                "auth.emailChange.cooldown",
                await ReadErrorCodesAsync(cooldown));
        }

        Assert.Equal(2, sender.Messages.Count);

        // Ровно через 60 секунд переотправка разрешена и отзывает прежнюю строку.
        clock.SetUtcNow(clock.UtcNow.AddSeconds(60));

        using (var second = await app.Client.PostAsJsonAsync(
                   "api/auth/email-change",
                   new RequestEmailChangeRequest(newEmail, TestApp.Password)))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        await app.RunEmailDispatchAsync();

        Assert.Equal(4, sender.Messages.Count);

        var confirmations = sender.Messages
            .Where(message => message.To == newEmail
                              && message.Subject == ConfirmationSubject)
            .Select(ExtractConfirmationLink)
            .ToList();

        Assert.Equal(2, confirmations.Count);

        var secondLink = Assert.Single(
            confirmations,
            candidate => candidate.Token != firstLink.Token);

        Assert.NotEqual(firstLink.Token, secondLink.Token);

        using var anonymous = app.CreateClient();

        // Переотправка отозвала прежний токен.
        using (var stale = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest(firstLink.Token)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
            Assert.Contains("auth.emailChange.invalid", await ReadErrorCodesAsync(stale));
        }

        // А новый токен из второго письма работает.
        using (var confirm = await anonymous.PostAsJsonAsync(
                   "api/auth/email-change/confirm",
                   new ConfirmEmailChangeRequest(secondLink.Token)))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }
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

    private static (string Link, string Token) ExtractConfirmationLink(
        CapturedEmailMessage message)
    {
        var match = Regex.Match(
            message.HtmlBody,
            @"href=""(?<link>http[^""]*/confirm-email-change\?token=(?<token>[A-Za-z0-9_-]+))""");

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
