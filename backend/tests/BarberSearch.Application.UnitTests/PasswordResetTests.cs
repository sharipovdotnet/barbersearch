using System.Text.RegularExpressions;
using BarberSearch.Application.EmailVerifications;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.PasswordResets;
using BarberSearch.Application.PasswordResets.CleanupPasswordResetTokens;
using BarberSearch.Application.PasswordResets.ConfirmPasswordReset;
using BarberSearch.Application.PasswordResets.RequestPasswordReset;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.PasswordResets;
using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Сценарии сброса пароля: запрос письма, кулдаун, анти-энумерация, отправка письма,
/// подтверждение одноразовой ссылки, политика пароля, отзыв сессий и уборка.
/// </summary>
/// <remarks>
/// Обработчики гоняются на фейковых репозиториях и фейковом отправителе: проверяется
/// решение, а не SMTP. Транзакция моделируется <see cref="FakeUnitOfWork"/> — снимком и
/// откатом, как реальная транзакция PostgreSQL.
/// </remarks>
public class PasswordResetTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string ValidPassword = "new-strong-password-42";

    [Fact]
    public async Task Request_WhenUserExists_SendsMessageWithHashedTokenAndLinkAfterCommit()
    {
        var context = new Context();

        // Адрес приходит «как ввёл пользователь»: обработчик обязан нормализовать его
        // до поиска и письма.
        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand($"  {Context.Email.ToUpperInvariant()}  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var message = Assert.Single(context.Sender.Messages);
        Assert.Equal(Context.Email, message.To);
        Assert.Equal(PasswordResetMessage.SUBJECT, message.Subject);

        var match = Regex.Match(
            message.HtmlBody,
            @"href=""(http://localhost:5173/reset-password\?token=([A-Za-z0-9_-]+))""");
        Assert.True(match.Success, message.HtmlBody);

        var link = match.Groups[1].Value;
        var rawToken = match.Groups[2].Value;

        Assert.Equal(PasswordResetTokenGenerator.TOKEN_LENGTH, rawToken.Length);
        Assert.Contains(link, message.TextBody, StringComparison.Ordinal);

        var stored = Assert.Single(context.StoredTokens);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.Equal(PasswordResetTokenGenerator.ComputeHash(rawToken), stored.TokenHash);
        Assert.Equal(context.User.Id, stored.UserId);
        Assert.Equal(Now, stored.IssuedAt);
        Assert.Equal(Now + PasswordResetLifetime.TOKEN_LIFETIME, stored.ExpiresAt);
        Assert.Null(stored.ConsumedAt);
        Assert.Null(stored.RevokedAt);

        // Публичный URL с завершающей косой чертой не даёт двойного слэша.
        Assert.DoesNotContain("//reset-password", link, StringComparison.Ordinal);

        // Срок в письме — ровно TTL среза, а не «примерно полчаса».
        Assert.Contains("30 минут", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("30 минут", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Задать новый пароль", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Задать новый пароль", message.TextBody, StringComparison.Ordinal);
        Assert.Contains(
            "Если вы не запрашивали сброс",
            message.TextBody,
            StringComparison.Ordinal);
        Assert.Contains("BarberSearch", message.TextBody, StringComparison.Ordinal);

        // Адрес, id и пароль в теле не появляются; открытый токен — только в ссылке.
        Assert.DoesNotContain(
            Context.Email,
            message.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Context.Email,
            message.TextBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            context.User.Id.ToString(),
            message.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            context.User.Id.ToString(),
            message.TextBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash:", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash:", message.TextBody, StringComparison.OrdinalIgnoreCase);

        // Отзыв/вставка — в одной транзакции, письмо — после коммита.
        Assert.True(context.Tokens.AddInTransaction);
        Assert.False(context.Sender.SentInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Equal(Context.Email, context.Users.LastGetByEmail);
    }

    [Fact]
    public async Task Request_WithPreviousActiveToken_RevokesItAndInsertsNewOneInsideTransaction()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = context.StoredTokens;
        Assert.Equal(2, stored.Count);

        var revoked = Assert.Single(stored, token => token.Id == previous.Id);
        Assert.Equal(Now, revoked.RevokedAt);
        Assert.Null(revoked.ConsumedAt);

        var active = Assert.Single(stored, token =>
            token.Id != previous.Id && token.RevokedAt is null && token.ConsumedAt is null);
        Assert.Equal(Now, active.IssuedAt);
        Assert.Equal(Now + PasswordResetLifetime.TOKEN_LIFETIME, active.ExpiresAt);

        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.True(context.Tokens.AddInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.False(context.Sender.SentInTransaction);
        Assert.Single(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_ForUnknownEmail_ReturnsOkAndSendsNothing()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand("unknown@example.com"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Request_WhenRepositoryReportsInvalidEmail_ReturnsOkAndSendsNothing()
    {
        var context = new Context();
        context.Users.GetByEmailBehavior = _ => new Failure(Error.Validation(
            ApiErrorCodes.User.EmailInvalid,
            "Email is invalid."));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_ForInactiveUser_ReturnsOkAndSendsNothing()
    {
        var context = new Context();
        Assert.True(context.User.Deactivate().IsSuccess);

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
        Assert.Equal(1, context.Users.GetByEmailCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Request_WithEmptyEmail_ReturnsOkWithoutRepositoryCalls(string email)
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Users.GetByEmailCallCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WithTooLongEmail_ReturnsOkWithoutRepositoryCalls()
    {
        var context = new Context();
        var email = new string('a', 250) + "@example.com";

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Users.GetByEmailCallCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Theory]
    [InlineData(false, "http://localhost:5173/")]
    [InlineData(true, null)]
    [InlineData(true, "   ")]
    public async Task Request_WhenEmailIsNotConfigured_ReturnsUnavailableBeforeUserLookup(
        bool emailEnabled,
        string? publicUrl)
    {
        var context = new Context(emailEnabled: emailEnabled, publicUrl: publicUrl);

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("auth.passwordReset.unavailable", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Users.GetByEmailCallCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Theory]
    [InlineData(59)]
    public async Task Request_WithinCooldown_ReturnsOkAndSendsNothing(int secondsAgo)
    {
        var context = new Context();
        context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
        Assert.Equal(0, context.Tokens.AddCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    public async Task Request_AtOrAfterCooldown_SendsAgainAndRevokesPrevious(int secondsAgo)
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Single(context.Sender.Messages);

        var storedPrevious = Assert.Single(
            context.StoredTokens,
            token => token.Id == previous.Id);
        Assert.Equal(Now, storedPrevious.RevokedAt);
    }

    [Fact]
    public async Task Request_WhenSenderFails_ReturnsOkKeepsTokenAndLogsWarningWithoutSecrets()
    {
        var context = new Context();
        context.Sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("smtp.unavailable", "SMTP is unreachable.")));

        var logger = new CapturingLogger<RequestPasswordResetHandler>();
        var handler = context.CreateRequester(logger);

        var result = await handler.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Письмо пытались отправить, но токен остался: через кулдаун можно запросить ещё раз.
        var attempted = Assert.Single(context.Sender.Messages);
        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(1, context.UnitOfWork.CommitCount);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(context.User.Id.ToString(), warning.Message, StringComparison.Ordinal);

        var rawToken = Regex.Match(
            attempted.HtmlBody,
            @"token=([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.NotEmpty(rawToken);
        Assert.DoesNotContain(Context.Email, warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawToken, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.TokenHash, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("passwordResetToken.issue.conflict")]
    [InlineData("passwordResetToken.update.concurrencyConflict")]
    public async Task Request_WhenActiveTokenRaceIsLost_ReturnsOkAndSendsNothing(string code)
    {
        var context = new Context();
        PasswordResetToken? previous = null;

        if (code.EndsWith(".concurrencyConflict", StringComparison.Ordinal))
        {
            previous = context.SeedToken(Now.AddMinutes(-5));
            context.Tokens.UpdateBehavior = _ => UnitResult.Failure(
                new Failure(Error.Conflict(code, "The token was modified concurrently.")));
        }
        else
        {
            context.Tokens.AddBehavior = _ => UnitResult.Failure(
                new Failure(Error.Conflict(code, "Another active token won the race.")));
        }

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Sender.Messages);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);

        if (previous is null)
        {
            Assert.Empty(context.StoredTokens);
            return;
        }

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(previous.Id, stored.Id);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task Request_WhenTransactionFailsWithAnotherError_PropagatesFailure()
    {
        var context = new Context();
        context.Tokens.AddBehavior = _ => UnitResult.Failure(
            new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "Unexpected database error.")));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.InternalDatabaseError, result.Error.Errors[0].Code);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WhenUserLookupFailsWithAnotherError_PropagatesFailure()
    {
        var context = new Context();
        context.Users.GetByEmailBehavior = _ => new Failure(Error.Failure(
            "database.unavailable",
            "The database is unavailable."));

        var result = await context.Requester.Handle(
            new RequestPasswordResetCommand(Context.Email),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("database.unavailable", result.Error.Errors[0].Code);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public void Message_EncodesFirstNameInHtmlAndKeepsPlainTextGreeting()
    {
        const string link = "https://barbersearch.test/reset-password?token=abc_DEF-123";

        var message = PasswordResetMessage.Create(
            "recipient@example.com",
            "<script>alert('x')</script>",
            link,
            30);

        Assert.Equal(PasswordResetMessage.SUBJECT, message.Subject);
        Assert.Equal("recipient@example.com", message.To);

        // HTML-версия экранирует имя; текстовый дубль остаётся читаемым.
        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("<script>", message.TextBody, StringComparison.Ordinal);

        // Ссылка присутствует в обеих версиях; пароль, email и id в теле не появляются.
        Assert.Contains(link, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(link, message.TextBody, StringComparison.Ordinal);
        Assert.Contains("30 минут", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("30 минут", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Задать новый пароль", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(
            "Если вы не запрашивали сброс",
            message.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "recipient@example.com",
            message.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirm_WithValidToken_ChangesPasswordConsumesTokenAndRevokesSessions()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        var refreshToken = await context.SeedRefreshTokenAsync();

        for (var attempt = 0; attempt < Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            Assert.True(context.User.RegisterFailedLogin(Now).IsSuccess);
        }

        Assert.True(context.User.IsLockedOut(Now));
        Assert.False(context.User.IsEmailVerified);

        var previousStamp = context.User.SecurityStamp;
        var userUpdateInTransaction = false;
        context.Users.UpdateBehavior = user =>
        {
            userUpdateInTransaction = context.UnitOfWork.IsInTransaction;
            return UnitResult.Success<Failure>();
        };

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal($"hash:{ValidPassword}", context.StoredUser.PasswordHash);
        Assert.NotEqual(previousStamp, context.StoredUser.SecurityStamp);
        Assert.True(context.StoredUser.IsEmailVerified);
        Assert.Equal(0, context.StoredUser.FailedLoginAttempts);
        Assert.Null(context.StoredUser.LastFailedLoginAt);
        Assert.Null(context.StoredUser.LockoutEnd);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(token.Id, stored.Id);
        Assert.Equal(Now, stored.ConsumedAt);
        Assert.Null(stored.RevokedAt);

        // Успешный сброс завершает все сессии: refresh-токен отозван в той же транзакции.
        Assert.Equal(Now, refreshToken.RevokedAt);
        Assert.Equal(1, context.RefreshTokens.RevokeAllCallCount);
        Assert.True(context.RefreshTokens.RevokeAllInTransaction);

        Assert.Equal(1, context.Passwords.HashCallCount);
        Assert.True(userUpdateInTransaction);
        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithUnknownToken_ReturnsInvalidAndChangesNothing()
    {
        var context = new Context();
        var previousStamp = context.User.SecurityStamp;

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(
                PasswordResetTokenGenerator.Generate(),
                ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(previousStamp, context.StoredUser.SecurityStamp);
        Assert.False(context.StoredUser.IsEmailVerified);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Users.UpdateCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithExpiredToken_ReturnsInvalidAndChangesNothing()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-31));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.False(token.IsActive(Now));
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Users.UpdateCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithUsedToken_ReturnsInvalidEvenThoughDomainConsumeIsIdempotent()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Consume(Now.AddMinutes(-1)).IsSuccess);

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Tokens.UpdateCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithRevokedToken_ReturnsInvalidAndChangesNothing()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Revoke(Now.AddMinutes(-1)).IsSuccess);

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithTokenOfMissingUser_ReturnsInvalidAndChangesNothing()
    {
        var context = new Context();
        var rawToken = PasswordResetTokenGenerator.Generate();
        var token = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            PasswordResetTokenGenerator.ComputeHash(rawToken),
            Now.AddMinutes(-5),
            Now.AddMinutes(25),
            Now).Value;
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Users.UpdateCallCount);
    }

    [Fact]
    public async Task Confirm_ForInactiveUser_ReturnsInvalidAndDoesNotReactivateAccount()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(context.User.Deactivate().IsSuccess);
        var previousStamp = context.User.SecurityStamp;

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.False(context.StoredUser.IsActive);
        Assert.Equal(previousStamp, context.StoredUser.SecurityStamp);
        Assert.False(context.StoredUser.IsEmailVerified);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Users.UpdateCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("abc+def")]
    public async Task Confirm_WithMalformedToken_ReturnsInvalidWithoutRepositoryCalls(
        string rawToken)
    {
        var context = new Context();

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Equal(0, context.Tokens.GetByHashCallCount);
        Assert.Equal(0, context.Users.GetByIdCallCount);
        Assert.Equal(0, context.Passwords.HashCallCount);
    }

    [Fact]
    public async Task Confirm_WithOverlongToken_ReturnsInvalidWithoutRepositoryCalls()
    {
        var context = new Context();

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(
                new string('a', PasswordResetTokenGenerator.MAX_TOKEN_LENGTH + 1),
                ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Equal(0, context.Tokens.GetByHashCallCount);
        Assert.Equal(0, context.Users.GetByIdCallCount);
        Assert.Equal(0, context.Passwords.HashCallCount);
    }

    [Theory]
    [InlineData("", "auth.password.required")]
    [InlineData("short-pass1", "auth.password.tooShort")]
    [InlineData("password", "auth.password.common")]
    public async Task Confirm_WithPasswordOutsidePolicy_ReturnsPolicyFailureWithoutConsumingToken(
        string password,
        string expectedCode)
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, password),
            CancellationToken.None);

        AssertPolicyFailure(result, expectedCode, context);
    }

    [Fact]
    public async Task Confirm_WithTooLongPassword_ReturnsPolicyFailureWithoutConsumingToken()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(
                rawToken,
                new string('a', PasswordPolicy.MAX_LENGTH + 1)),
            CancellationToken.None);

        AssertPolicyFailure(result, "auth.password.tooLong", context);
    }

    [Fact]
    public async Task Confirm_WithPasswordEqualToEmail_ReturnsPolicyFailureWithoutConsumingToken()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, Context.Email),
            CancellationToken.None);

        AssertPolicyFailure(result, "auth.password.equalsEmail", context);
    }

    [Fact]
    public async Task Confirm_WithPasswordEqualToName_ReturnsPolicyFailureWithoutConsumingToken()
    {
        const string firstName = "Иван-Иванович";
        var context = new Context(firstName: firstName);
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, firstName),
            CancellationToken.None);

        AssertPolicyFailure(result, "auth.password.equalsName", context);
    }

    [Fact]
    public async Task Confirm_WhenTokenRepositoryFailsWithAnotherError_PropagatesFailure()
    {
        var context = new Context();
        context.Tokens.GetByHashBehavior = _ => new Failure(Error.Failure(
            "database.unavailable",
            "The database is unavailable."));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(
                PasswordResetTokenGenerator.Generate(),
                ValidPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("database.unavailable", result.Error.Errors[0].Code);
        Assert.NotEqual(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(0, context.Passwords.HashCallCount);
    }

    [Fact]
    public async Task Confirm_WhenUserRepositoryFailsWithAnotherError_PropagatesFailure()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));
        context.Users.GetByIdBehavior = _ => new Failure(Error.Conflict(
            "user.lookup.conflict",
            "The user could not be loaded because of a conflict."));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.lookup.conflict", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Passwords.HashCallCount);
    }

    [Fact]
    public async Task Confirm_WhenConcurrentRequestConsumedToken_ReturnsInvalidInsteadOfServerError()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));
        var previousStamp = context.User.SecurityStamp;

        context.Tokens.UpdateBehavior = _ => UnitResult.Failure(
            new Failure(Error.Conflict(
                "passwordResetToken.update.concurrencyConflict",
                "The token was modified by another request.")));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        AssertInvalid(result);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);
        Assert.Equal("hash:old-password", context.StoredUser.PasswordHash);
        Assert.Equal(previousStamp, context.StoredUser.SecurityStamp);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task Confirm_ForAlreadyVerifiedUser_StillChangesPassword()
    {
        var context = new Context();
        Assert.True(context.User.MarkEmailAsVerified().IsSuccess);
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"hash:{ValidPassword}", context.StoredUser.PasswordHash);
        Assert.Equal(Now, context.StoredTokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task Confirm_DoesNotDependOnEmailConfiguration()
    {
        var context = new Context(emailEnabled: false, publicUrl: null);
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmPasswordResetCommand(rawToken, ValidPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"hash:{ValidPassword}", context.StoredUser.PasswordHash);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyRowsOlderThanRetentionAndPassesBoundary()
    {
        var context = new Context();

        // Истёк 39 дней назад — за границей 30-дневного хранения, удаляется.
        context.SeedToken(Now.AddDays(-40), TimeSpan.FromDays(1));

        // Использован 35 дней назад, хотя срок ещё не истёк — удаляется по consumed_at.
        var consumedLongAgo = PasswordResetToken.Create(
            context.User.Id,
            PasswordResetTokenGenerator.ComputeHash(PasswordResetTokenGenerator.Generate()),
            Now.AddDays(-40),
            Now.AddDays(20),
            Now.AddDays(-40)).Value;
        Assert.True(consumedLongAgo.Consume(Now.AddDays(-35)).IsSuccess);
        context.Tokens.AddRaw(consumedLongAgo);

        // Недавно истёк — остаётся.
        context.SeedToken(Now.AddDays(-2), TimeSpan.FromHours(1));

        // Активный — остаётся: по нему считается кулдаун.
        var active = context.SeedToken(Now);

        var handler = new CleanupPasswordResetTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupPasswordResetTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupPasswordResetTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Deleted);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Contains(context.StoredTokens, token => token.Id == active.Id);

        // Обработчик передал именно бизнес-«сейчас» и ретенцию слоя Application.
        Assert.Equal(1, context.Tokens.DeleteCallCount);
        Assert.Equal(Now, context.Tokens.LastDeleteNow);
        Assert.Equal(PasswordResetLifetime.TOKEN_RETENTION, context.Tokens.LastDeleteRetention);
    }

    [Fact]
    public async Task Cleanup_WithoutStaleRows_DeletesNothing()
    {
        var context = new Context();
        context.SeedToken(Now);

        var handler = new CleanupPasswordResetTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupPasswordResetTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupPasswordResetTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Deleted);
        Assert.Single(context.StoredTokens);
    }

    [Fact]
    public async Task Cleanup_WhenDatabaseFails_LogsAndDoesNotThrow()
    {
        var context = new Context();
        context.Tokens.DeleteException = new TimeoutException("The database timed out.");
        var logger = new CapturingLogger<CleanupPasswordResetTokensHandler>();

        var handler = new CleanupPasswordResetTokensHandler(
            context.Tokens,
            context.Clock,
            logger);

        var result = await handler.Handle(
            new CleanupPasswordResetTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Deleted);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("cleanup", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInvalid(UnitResult<Failure> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("auth.passwordReset.invalid", result.Error.Errors[0].Code);
    }

    private static void AssertPolicyFailure(
        UnitResult<Failure> result,
        string expectedCode,
        Context context)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("newPassword", result.Error.Errors[0].InvalidField);

        // Политика не потребляет токен: с валидным паролем ссылку можно использовать снова.
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal(0, context.Passwords.HashCallCount);
        Assert.Equal(0, context.Users.UpdateCallCount);
        Assert.Equal(0, context.Tokens.UpdateCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    /// <summary>
    /// Общий сценарий среза: один пользователь, фейковые репозитории и часы.
    /// </summary>
    private sealed class Context
    {
        public const string Email = "client@example.com";

        private readonly EmailOptions _emailOptions;
        private readonly AppOptions _appOptions;

        public Context(
            bool emailEnabled = true,
            string? publicUrl = "http://localhost:5173/",
            string firstName = "Иван",
            string lastName = "Петров")
        {
            User = User.Create(Email, "hash:old-password", firstName, lastName).Value;
            Users.Add(User);

            Tokens = new FakePasswordResetTokenRepository();
            RefreshTokens = new FakeRefreshTokenRepository();
            UnitOfWork = new FakeUnitOfWork(Users, Tokens, RefreshTokens);
            Tokens.IsInTransaction = () => UnitOfWork.IsInTransaction;
            Sender.IsInTransaction = () => UnitOfWork.IsInTransaction;
            RefreshTokens.IsInTransaction = () => UnitOfWork.IsInTransaction;

            _emailOptions = new EmailOptions
            {
                Enabled = emailEnabled,
                Host = "localhost",
                Port = 1025,
                UseTls = false,
                FromAddress = "no-reply@barbersearch.test"
            };

            _appOptions = new AppOptions { PublicUrl = publicUrl };

            Requester = CreateRequester(NullLogger<RequestPasswordResetHandler>.Instance);

            Confirmer = new ConfirmPasswordResetHandler(
                Users,
                Tokens,
                Passwords,
                Policy,
                RefreshTokens,
                UnitOfWork,
                Clock,
                NullLogger<ConfirmPasswordResetHandler>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new(Now);

        public FakeUserRepository Users { get; } = new();

        public FakePasswordResetTokenRepository Tokens { get; }

        public FakeRefreshTokenRepository RefreshTokens { get; }

        public FakePasswordService Passwords { get; } = new();

        public IPasswordPolicy Policy { get; } = new PasswordPolicy();

        public FakeEmailSender Sender { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; }

        public User User { get; }

        public RequestPasswordResetHandler Requester { get; }

        public ConfirmPasswordResetHandler Confirmer { get; }

        public User StoredUser => Users.Users.Single();

        public IReadOnlyCollection<PasswordResetToken> StoredTokens => Tokens.Tokens;

        /// <summary>
        /// Создаёт обработчик запроса с подменённым журналом: тест проверяет, что в лог
        /// попадают только <c>userId</c> и коды ошибок.
        /// </summary>
        public RequestPasswordResetHandler CreateRequester(
            ILogger<RequestPasswordResetHandler> logger)
        {
            return new RequestPasswordResetHandler(
                Users,
                Tokens,
                UnitOfWork,
                Sender,
                _emailOptions,
                _appOptions,
                Clock,
                logger);
        }

        /// <summary>
        /// Кладёт в «таблицу» активный токен, выданный <paramref name="issuedAt"/>.
        /// </summary>
        public PasswordResetToken SeedToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var token = PasswordResetToken.Create(
                User.Id,
                PasswordResetTokenGenerator.ComputeHash(PasswordResetTokenGenerator.Generate()),
                issuedAt,
                issuedAt + (lifetime ?? PasswordResetLifetime.TOKEN_LIFETIME),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return token;
        }

        /// <summary>
        /// Кладёт активный токен и возвращает открытое значение вместе со строкой.
        /// </summary>
        public (string RawToken, PasswordResetToken Token) SeedRawToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var rawToken = PasswordResetTokenGenerator.Generate();
            var token = PasswordResetToken.Create(
                User.Id,
                PasswordResetTokenGenerator.ComputeHash(rawToken),
                issuedAt,
                issuedAt + (lifetime ?? PasswordResetLifetime.TOKEN_LIFETIME),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return (rawToken, token);
        }

        /// <summary>
        /// Кладёт живой refresh-токен пользователя: успешный сброс обязан его отозвать.
        /// </summary>
        public async Task<RefreshToken> SeedRefreshTokenAsync()
        {
            var token = RefreshToken.Create(
                User.Id,
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Now.AddDays(7),
                Now).Value;

            await RefreshTokens.AddAsync(token);

            return token;
        }
    }

    /// <summary>
    /// Журнал в памяти: тест проверяет уровень и текст записи обработчика.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
