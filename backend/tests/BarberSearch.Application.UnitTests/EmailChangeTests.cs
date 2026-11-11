using System.Text.RegularExpressions;
using BarberSearch.Application.EmailChanges;
using BarberSearch.Application.EmailChanges.CleanupEmailChangeTokens;
using BarberSearch.Application.EmailChanges.ConfirmEmailChange;
using BarberSearch.Application.EmailChanges.RequestEmailChange;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailChanges;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Сценарии смены email: заявка с проверкой текущего пароля, кулдаун, оба письма в outbox,
/// подтверждение ссылкой, гонки и уборка.
/// </summary>
/// <remarks>
/// Обработчики гоняются на фейковых репозиториях: проверяется решение, а не SQL. Транзакция
/// моделируется <see cref="FakeUnitOfWork"/> — снимком и откатом, как реальная транзакция
/// PostgreSQL. Открытый токен существует только в собранном письме; в «таблице» лежит его
/// SHA-256.
/// </remarks>
public class EmailChangeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Request_QueuesConfirmationToNewAddressAndNoticeToOldAddressInOneTransaction()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Письма не отправляются из запроса: их заберёт диспетчер outbox.
        Assert.Equal(0, context.Sender.SendCallCount);
        Assert.Equal(1, context.UnitOfWork.CommitCount);

        var confirmation = Assert.Single(
            context.Outbox.Messages,
            message => message.To == Context.NewAddress);
        Assert.Equal(EmailChangeConfirmationMessage.SUBJECT, confirmation.Subject);

        var notice = Assert.Single(
            context.Outbox.Messages,
            message => message.To == Context.OldAddress);
        Assert.Equal(EmailChangeNoticeMessage.SUBJECT, notice.Subject);

        // Оба письма — строки одной транзакции вместе с токеном.
        Assert.True(context.Tokens.AddInTransaction);
        Assert.True(context.Outbox.AddInTransaction);
        Assert.Equal(2, context.Outbox.AddCallCount);

        var token = Assert.Single(context.StoredTokens);
        Assert.Equal(Context.NewAddress, token.NewEmail);
        Assert.Equal(context.User.Id, token.UserId);
        Assert.Equal(Now, token.IssuedAt);
        Assert.Equal(Now + EmailChangeLifetime.TOKEN_LIFETIME, token.ExpiresAt);

        // Ключ ссылки — id токена, у уведомления свой префикс: иначе вторая строка
        // столкнулась бы с первой по ux_email_outbox_deduplication_key.
        var rows = context.Outbox.Rows;
        Assert.Contains(
            rows,
            row => row.DeduplicationKey == token.Id.ToString("D"));
        Assert.Contains(
            rows,
            row => row.DeduplicationKey == $"email-change-notice:{token.Id:D}");

        // Все строки — готовые к отправке, но ещё не отправленные.
        Assert.All(rows, row =>
        {
            Assert.Equal(0, row.Attempts);
            Assert.Null(row.SentAt);
            Assert.Null(row.FailedAt);
            Assert.Equal(Now, row.NextAttemptAt);
            Assert.True(row.DeduplicationKey.Length <= 128);
        });
    }

    [Fact]
    public async Task Request_ConfirmationContainsLinkWithRawTokenWhileDatabaseKeepsOnlyHash()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var confirmation = Assert.Single(
            context.Outbox.Messages,
            message => message.To == Context.NewAddress);

        var match = Regex.Match(
            confirmation.HtmlBody,
            @"href=""(http://localhost:5173/confirm-email-change\?token=([A-Za-z0-9_-]+))""");
        Assert.True(match.Success, confirmation.HtmlBody);

        var link = match.Groups[1].Value;
        var rawToken = match.Groups[2].Value;

        Assert.Equal(43, rawToken.Length);
        Assert.Contains(link, confirmation.TextBody, StringComparison.Ordinal);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(EmailChangeTokenGenerator.ComputeHash(rawToken), stored.TokenHash);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.DoesNotContain(rawToken, stored.TokenHash, StringComparison.Ordinal);

        // Адрес и id пользователя в тело письма не попадают сверх адресата.
        Assert.DoesNotContain(
            context.User.Id.ToString(),
            confirmation.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_NoticeToOldAddressHasNoTokenOrConfirmationLink()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var confirmation = Assert.Single(
            context.Outbox.Messages,
            message => message.To == Context.NewAddress);

        var match = Regex.Match(
            confirmation.HtmlBody,
            @"confirm-email-change\?token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, confirmation.HtmlBody);

        var rawToken = match.Groups[1].Value;

        var notice = Assert.Single(
            context.Outbox.Messages,
            message => message.To == Context.OldAddress);

        // Уведомление — защита владельца старого адреса, а не второй канал подтверждения.
        Assert.DoesNotContain(rawToken, notice.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToken, notice.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm-email-change", notice.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", notice.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", notice.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", notice.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_NormalizesNewAddressBeforeStoringAndSending()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                "  New@Example.COM  ",
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Context.NewAddress, Assert.Single(context.StoredTokens).NewEmail);

        Assert.Contains(
            context.Outbox.Messages,
            message => message.To == Context.NewAddress);
    }

    [Fact]
    public async Task Request_WithPreviousActiveToken_RevokesItAndInsertsNewOneInsideTransaction()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = context.StoredTokens;
        Assert.Equal(2, stored.Count);

        var revoked = Assert.Single(stored, token => token.Id == previous.Id);
        Assert.Equal(Now, revoked.RevokedAt);
        Assert.Null(revoked.ConsumedAt);

        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.True(context.Tokens.AddInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.Equal(2, context.Outbox.Rows.Count);
    }

    [Theory]
    [InlineData(59)]
    public async Task Request_WithinCooldown_ReturnsCooldownAndWritesNothing(int secondsAgo)
    {
        var context = new Context();
        context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.Auth.EmailChangeCooldown, error.Code);

        Assert.Single(context.StoredTokens);
        Assert.Empty(context.Outbox.Rows);
        Assert.Equal(0, context.Tokens.AddCallCount);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    public async Task Request_AtOrAfterCooldown_RevokesPreviousAndSendsAgain(int secondsAgo)
    {
        var context = new Context();
        context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Equal(2, context.Outbox.Rows.Count);
    }

    [Fact]
    public async Task Request_CountsCooldownFromIssuedAtNotCreatedAt()
    {
        var context = new Context();

        // Строка создана только что системными часами, но бизнес-время выдачи — два часа
        // назад: кулдаун давно прошёл, повторная заявка разрешена.
        context.SeedToken(Now.AddHours(-2), TimeSpan.FromHours(22));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Outbox.Rows.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("Ivan <ivan@example.com>")]
    public async Task Request_WithBrokenNewAddress_ReturnsValidationBeforePasswordCheck(
        string newAddress)
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(context.User.Id, newAddress, "whatever"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(ApiErrorCodes.User.ChangeEmailNewEmail, error.Code);
        Assert.Equal("newEmail", error.InvalidField);

        // Дешёвые проверки стоят до PBKDF2: пароль даже не сверялся.
        Assert.Equal(0, context.Passwords.VerifyCallCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Outbox.Rows);
    }

    [Theory]
    [InlineData("client@example.com")]
    [InlineData("  Client@Example.COM  ")]
    public async Task Request_WithSameAddress_ReturnsSameEmailBeforePasswordCheck(string newAddress)
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(context.User.Id, newAddress, "whatever"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(ApiErrorCodes.User.ChangeEmailSameEmail, error.Code);
        Assert.Equal("newEmail", error.InvalidField);
        Assert.Equal(0, context.Passwords.VerifyCallCount);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_WithWrongCurrentPassword_ReturnsInvalidPassword()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                "WrongPassword1!"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(ApiErrorCodes.Auth.EmailChangeInvalidCurrentPassword, error.Code);
        Assert.Equal("currentPassword", error.InvalidField);

        Assert.Equal(1, context.Passwords.VerifyCallCount);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_WithWrongPasswordAndTakenAddress_ReportsPasswordFirst()
    {
        var context = new Context();
        context.Users.Add(CreateUser("taken@example.com"));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                "taken@example.com",
                "WrongPassword1!"),
            CancellationToken.None);

        Assert.True(result.IsFailure);

        // Ответ «адрес занят» не должен доставаться тому, кто не подтвердил личность.
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalidCurrentPassword,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Request_WithTakenAddress_ReturnsConflictWithoutWriting()
    {
        var context = new Context();
        context.Users.Add(CreateUser("taken@example.com"));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                "taken@example.com",
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.User.ChangeEmailEmailTaken, error.Code);

        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Outbox.Rows);
        Assert.Equal(0, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Request_WithTakenAddressAndDisabledEmail_ReportsTakenAddressFirst()
    {
        var context = new Context(emailEnabled: false);
        context.Users.Add(CreateUser("taken@example.com"));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                "taken@example.com",
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.User.ChangeEmailEmailTaken,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Request_WhenEmailIsDisabled_ReturnsUnavailable()
    {
        var context = new Context(emailEnabled: false);

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.Auth.EmailChangeUnavailable, error.Code);

        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Outbox.Rows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Request_WhenPublicUrlIsBlank_ReturnsUnavailable(string? publicUrl)
    {
        var context = new Context(publicUrl: publicUrl);

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeUnavailable,
            Assert.Single(result.Error.Errors).Code);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_WhenEmailIsUnavailableAndTokenIsRecent_ReportsUnavailableBeforeCooldown()
    {
        var context = new Context(emailEnabled: false);
        context.SeedToken(Now.AddSeconds(-5));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);

        // Глобальное состояние сервера проверяется до персонального кулдауна.
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeUnavailable,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Request_WhenUserDoesNotExist_ReturnsNotFound()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                Guid.CreateVersion7(),
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, Assert.Single(result.Error.Errors).Type);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_ForMissingUserWithBrokenAddress_ReportsUserFirst()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                Guid.CreateVersion7(),
                "not-an-email",
                Context.CurrentPassword),
            CancellationToken.None);

        // Порядок фиксирован: пользователь проверяется раньше синтаксиса адреса.
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, Assert.Single(result.Error.Errors).Type);
        Assert.Equal(0, context.Passwords.VerifyCallCount);
    }

    [Fact]
    public async Task Request_WhenOutboxInsertFails_RollsBackTokenAndRevocation()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));
        context.Outbox.AddBehavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("emailOutbox.add.failed", "Insert failed.")));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);

        // Откат вернул и отзыв прежнего токена, и вставку нового: ни токена, ни письма.
        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(previous.Id, stored.Id);
        Assert.Null(stored.RevokedAt);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_WhenTokenInsertFails_RollsBackRevokedToken()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));
        context.Tokens.AddBehavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("emailChange.add.failed", "Insert failed.")));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(previous.Id, stored.Id);
        Assert.Null(stored.RevokedAt);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Request_WhenConcurrentRequestWinsTheActiveIndex_ReturnsCooldown()
    {
        var context = new Context();

        // Окно между проверкой кулдауна и записью: конкурирующая заявка уже вставила
        // активную строку, и вторая упирается в ux_email_change_tokens_active.
        context.Tokens.OnAdd = _ => context.Tokens.AddRaw(context.SeedToken(Now));

        var result = await context.Requester.Handle(
            new RequestEmailChangeCommand(
                context.User.Id,
                Context.NewAddress,
                Context.CurrentPassword),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.Auth.EmailChangeCooldown, error.Code);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Confirm_WithValidToken_ChangesAddressAndEndsEverySession()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        var beforeStamp = context.StoredUser.SecurityStamp;

        var userUpdateInTransaction = false;
        context.Users.UpdateBehavior = user =>
        {
            userUpdateInTransaction = context.UnitOfWork.IsInTransaction;

            return UnitResult.Success<Failure>();
        };

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var user = context.StoredUser;
        Assert.Equal(Context.NewAddress, user.Email);
        Assert.True(user.IsEmailVerified);
        Assert.NotEqual(beforeStamp, user.SecurityStamp);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(token.Id, stored.Id);
        Assert.Equal(Now, stored.ConsumedAt);
        Assert.Null(stored.RevokedAt);

        // Пользователь, токен и отзыв refresh-токенов — в одной транзакции.
        Assert.True(userUpdateInTransaction);
        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.Equal(1, context.RefreshTokens.RevokeAllCallCount);
        Assert.True(context.RefreshTokens.RevokeAllInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_Repeated_IsIdempotentAndDoesNotWriteAgain()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        Assert.True((await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None)).IsSuccess);

        var tokenUpdates = context.Tokens.UpdateCallCount;
        var userUpdates = context.Users.UpdateCallCount;
        var revokeCalls = context.RefreshTokens.RevokeAllCallCount;

        var repeated = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(repeated.IsSuccess);
        Assert.Equal(Context.NewAddress, context.StoredUser.Email);
        Assert.Equal(tokenUpdates, context.Tokens.UpdateCallCount);
        Assert.Equal(userUpdates, context.Users.UpdateCallCount);
        Assert.Equal(revokeCalls, context.RefreshTokens.RevokeAllCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("abc+def")]
    [InlineData(null)]
    public async Task Confirm_WithMalformedToken_ReturnsInvalidWithoutRepositoryCalls(
        string? rawToken)
    {
        var context = new Context();

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken ?? string.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
        Assert.Equal(0, context.Tokens.GetByHashCallCount);
        Assert.Equal(0, context.Users.GetByIdCallCount);
    }

    [Fact]
    public async Task Confirm_WithUnknownToken_ReturnsInvalid()
    {
        var context = new Context();

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(EmailChangeTokenGenerator.Generate()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Confirm_WithTokenOfMissingUser_ReturnsInvalid()
    {
        var context = new Context();
        var rawToken = EmailChangeTokenGenerator.Generate();

        context.Tokens.AddRaw(CreateToken(Guid.CreateVersion7(), rawToken, Now));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Confirm_WithExpiredToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddDays(-2), TimeSpan.FromHours(24));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
        Assert.Equal(Context.OldAddress, context.StoredUser.Email);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task Confirm_WithConsumedToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Consume(Now.AddMinutes(-1)).IsSuccess);
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
        Assert.Equal(Context.OldAddress, context.StoredUser.Email);
    }

    [Fact]
    public async Task Confirm_WithRevokedToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Revoke(Now.AddMinutes(-1)).IsSuccess);
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Auth.EmailChangeInvalid,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task Confirm_WithTakenAddress_ReturnsConflictAndChangesNothing()
    {
        var context = new Context();
        context.Users.Add(CreateUser(Context.NewAddress));
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.User.ChangeEmailEmailTaken, error.Code);

        Assert.Equal(Context.OldAddress, context.StoredUser.Email);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal(0, context.RefreshTokens.RevokeAllCallCount);
    }

    [Fact]
    public async Task Confirm_WhenAddressAlreadyApplied_ReturnsSuccessEvenForExpiredToken()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddDays(-2), TimeSpan.FromHours(24));

        // Адрес уже применён (как после успешного первого клика): проверка идемпотентности
        // стоит раньше срока токена, поэтому старая ссылка отвечает успехом, а не 400.
        Assert.True(context.User.ChangeEmail(Context.NewAddress).IsSuccess);

        var updates = context.Tokens.UpdateCallCount;
        var revokes = context.RefreshTokens.RevokeAllCallCount;

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(updates, context.Tokens.UpdateCallCount);
        Assert.Equal(revokes, context.RefreshTokens.RevokeAllCallCount);
    }

    [Fact]
    public async Task Confirm_WhenConcurrentConfirmWon_ReturnsSuccessInsteadOfConflict()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        // Второй confirm проиграл optimistic concurrency, но параллельный запрос уже
        // применил адрес: для клиента это успех. Фейковая транзакция без участников
        // не откатывает пользователя — как уже зафиксированный COMMIT победителя.
        var handler = new ConfirmEmailChangeHandler(
            context.Users,
            context.Tokens,
            context.RefreshTokens,
            new FakeUnitOfWork(),
            context.Clock,
            NullLogger<ConfirmEmailChangeHandler>.Instance);

        context.Users.UpdateBehavior = _ => UnitResult.Failure(new Failure(Error.Conflict(
            ApiErrorCodes.User.UpdateConcurrencyConflict,
            "The user was modified by another request.")));

        var result = await handler.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Context.NewAddress, context.StoredUser.Email);
    }

    [Fact]
    public async Task Confirm_WhenAddressWasTakenInRace_ReturnsEmailTaken()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        // Уникальный индекс ux_users_email сработал позже проверки занятости.
        context.Users.UpdateBehavior = _ => UnitResult.Failure(new Failure(Error.Conflict(
            "user.update.uniqueViolation",
            "duplicate key value violates unique constraint \"ux_users_email\"")));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.User.ChangeEmailEmailTaken, error.Code);
    }

    [Fact]
    public async Task Confirm_ForInactiveUser_StillChangesAddress()
    {
        var context = new Context();
        Assert.True(context.User.Deactivate().IsSuccess);
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        // Осознанное поведение, как у подтверждения email: владение адресом не зависит
        // от состояния аккаунта, а деактивированный пользователь всё равно не войдёт.
        Assert.True(result.IsSuccess);
        Assert.Equal(Context.NewAddress, context.StoredUser.Email);
        Assert.False(context.StoredUser.IsActive);
    }

    [Fact]
    public async Task Confirm_WhenTokenUpdateFails_RollsBackUserChange()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));
        context.Tokens.UpdateBehavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("emailChange.update.failed", "Update failed.")));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);
        Assert.Equal(Context.OldAddress, context.StoredUser.Email);
        Assert.False(context.StoredUser.IsEmailVerified);
        Assert.Equal(0, context.RefreshTokens.RevokeAllCallCount);
    }

    [Fact]
    public async Task Confirm_WhenRefreshRevocationFails_RollsBackAddressAndToken()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));
        context.RefreshTokens.RevokeAllBehavior = (_, _) => UnitResult.Failure(
            new Failure(Error.Failure("refreshTokens.revoke.failed", "Revoke failed.")));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailChangeCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);

        // Частичного результата «адрес сменён, старые сессии живы» быть не должно.
        Assert.Equal(Context.OldAddress, context.StoredUser.Email);
        Assert.False(context.StoredUser.IsEmailVerified);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyRowsOlderThanRetention()
    {
        var context = new Context();

        // Истёк 40 дней назад — за границей 30-дневного хранения, удаляется.
        context.SeedToken(Now.AddDays(-40), TimeSpan.FromDays(1));

        // Использован 35 дней назад, но срок ещё не истёк — удаляется по consumed_at.
        var consumedLongAgo = CreateToken(
            context.User.Id,
            EmailChangeTokenGenerator.Generate(),
            Now.AddDays(-40),
            TimeSpan.FromDays(60));
        Assert.True(consumedLongAgo.Consume(Now.AddDays(-35)).IsSuccess);
        context.Tokens.AddRaw(consumedLongAgo);

        // Недавно истёк — остаётся.
        context.SeedToken(Now.AddDays(-2), TimeSpan.FromHours(1));

        // Активный — остаётся: по нему считается кулдаун.
        context.SeedToken(Now, TimeSpan.FromHours(24));

        var handler = new CleanupEmailChangeTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupEmailChangeTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupEmailChangeTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Deleted);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Contains(
            context.StoredTokens,
            token => token.ConsumedAt is null && token.RevokedAt is null);
    }

    [Fact]
    public async Task Cleanup_WithoutStaleRows_DeletesNothing()
    {
        var context = new Context();
        context.SeedToken(Now, TimeSpan.FromHours(24));

        var handler = new CleanupEmailChangeTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupEmailChangeTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupEmailChangeTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Deleted);
        Assert.Single(context.StoredTokens);
    }

    [Fact]
    public void ConfirmationMessage_HtmlEncodesNameAndKeepsLink()
    {
        const string link = "http://localhost:5173/confirm-email-change?token=abc";
        var message = EmailChangeConfirmationMessage.Create(
            Context.NewAddress,
            "<b>Иван</b>",
            Context.NewAddress,
            link,
            24);

        Assert.Equal(EmailChangeConfirmationMessage.SUBJECT, message.Subject);
        Assert.Equal(Context.NewAddress, message.To);
        Assert.Contains("&lt;b&gt;Иван&lt;/b&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Иван</b>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("24", message.TextBody, StringComparison.Ordinal);
        Assert.Contains(link, message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticeMessage_HasNoLinkOrToken()
    {
        var message = EmailChangeNoticeMessage.Create(
            Context.OldAddress,
            "Иван",
            Context.NewAddress);

        Assert.Equal(EmailChangeNoticeMessage.SUBJECT, message.Subject);
        Assert.Equal(Context.OldAddress, message.To);
        Assert.Contains(Context.NewAddress, message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm-email-change", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("http", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_HaveDistinctSubjects()
    {
        Assert.NotEqual(
            EmailChangeConfirmationMessage.SUBJECT,
            EmailChangeNoticeMessage.SUBJECT);
    }

    [Fact]
    public void Lifetime_MatchesEmailVerificationWindows()
    {
        Assert.Equal(TimeSpan.FromHours(24), EmailChangeLifetime.TOKEN_LIFETIME);
        Assert.Equal(TimeSpan.FromSeconds(60), EmailChangeLifetime.RESEND_COOLDOWN);
        Assert.Equal(TimeSpan.FromDays(30), EmailChangeLifetime.TOKEN_RETENTION);
    }

    private static User CreateUser(string email)
        => User.Create(email, $"hash:{Context.CurrentPassword}", "Другой", "Пользователь").Value;

    private static EmailChangeToken CreateToken(
        Guid userId,
        string rawToken,
        DateTimeOffset issuedAt,
        TimeSpan? lifetime = null)
        => EmailChangeToken.Create(
            userId,
            Context.NewAddress,
            EmailChangeTokenGenerator.ComputeHash(rawToken),
            issuedAt,
            issuedAt + (lifetime ?? TimeSpan.FromHours(24)),
            issuedAt).Value;

    /// <summary>
    /// Общий сценарий: один пользователь, фейковые репозитории и часы.
    /// </summary>
    private sealed class Context
    {
        public const string OldAddress = "client@example.com";

        public const string NewAddress = "new@example.com";

        public const string CurrentPassword = "Current!Passw0rd";

        public Context(bool emailEnabled = true, string? publicUrl = "http://localhost:5173/")
        {
            User = User.Create(
                OldAddress,
                $"hash:{CurrentPassword}",
                "Иван",
                "Петров").Value;
            Users.Add(User);

            Tokens = new FakeEmailChangeTokenRepository();
            Outbox = new FakeEmailOutboxRepository();
            RefreshTokens = new FakeRefreshTokenRepository();
            UnitOfWork = new FakeUnitOfWork(Users, Tokens, Outbox, RefreshTokens);
            Tokens.IsInTransaction = () => UnitOfWork.IsInTransaction;
            Outbox.IsInTransaction = () => UnitOfWork.IsInTransaction;
            RefreshTokens.IsInTransaction = () => UnitOfWork.IsInTransaction;

            Sender.IsAvailable = emailEnabled;

            Requester = new RequestEmailChangeHandler(
                Users,
                Tokens,
                Outbox,
                UnitOfWork,
                Passwords,
                Sender,
                new AppOptions { PublicUrl = publicUrl },
                Clock,
                NullLogger<RequestEmailChangeHandler>.Instance);

            Confirmer = new ConfirmEmailChangeHandler(
                Users,
                Tokens,
                RefreshTokens,
                UnitOfWork,
                Clock,
                NullLogger<ConfirmEmailChangeHandler>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new(Now);

        public FakeUserRepository Users { get; } = new();

        public FakeEmailChangeTokenRepository Tokens { get; }

        public FakeEmailOutboxRepository Outbox { get; }

        public FakeRefreshTokenRepository RefreshTokens { get; }

        public FakeEmailSender Sender { get; } = new();

        public FakePasswordService Passwords { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; }

        public User User { get; }

        public RequestEmailChangeHandler Requester { get; }

        public ConfirmEmailChangeHandler Confirmer { get; }

        public User StoredUser => Users.Users.Single(user => user.Id == User.Id);

        public IReadOnlyCollection<EmailChangeToken> StoredTokens => Tokens.Tokens;

        /// <summary>
        /// Кладёт в «таблицу» активный токен, выданный <paramref name="issuedAt"/>.
        /// </summary>
        public EmailChangeToken SeedToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var token = EmailChangeToken.Create(
                User.Id,
                NewAddress,
                EmailChangeTokenGenerator.ComputeHash(EmailChangeTokenGenerator.Generate()),
                issuedAt,
                issuedAt + (lifetime ?? TimeSpan.FromHours(24)),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return token;
        }

        /// <summary>
        /// Кладёт активный токен и возвращает открытое значение вместе со строкой.
        /// </summary>
        public (string RawToken, EmailChangeToken Token) SeedRawToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var rawToken = EmailChangeTokenGenerator.Generate();
            var token = EmailChangeToken.Create(
                User.Id,
                NewAddress,
                EmailChangeTokenGenerator.ComputeHash(rawToken),
                issuedAt,
                issuedAt + (lifetime ?? TimeSpan.FromHours(24)),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return (rawToken, token);
        }
    }
}
