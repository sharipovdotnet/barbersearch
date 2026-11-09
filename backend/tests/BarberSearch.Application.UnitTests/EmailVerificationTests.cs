using System.Text.RegularExpressions;
using BarberSearch.Application.EmailVerifications;
using BarberSearch.Application.EmailVerifications.CleanupEmailVerificationTokens;
using BarberSearch.Application.EmailVerifications.ConfirmEmailVerification;
using BarberSearch.Application.EmailVerifications.RequestEmailVerification;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.EmailVerifications;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Сценарии подтверждения email: запрос письма, кулдаун, конфигурация почты, подтверждение
/// токена, гонка двух подтверждений и уборка.
/// </summary>
/// <remarks>
/// Обработчики гоняются на фейковых репозиториях и фейковом отправителе: проверяется
/// решение, а не SMTP. Транзакция моделируется <see cref="FakeUnitOfWork"/> — снимком и
/// откатом, как реальная транзакция PostgreSQL.
/// </remarks>
public class EmailVerificationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Request_WithPreviousActiveToken_RevokesItAndInsertsNewOneInsideTransaction()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = context.StoredTokens;
        Assert.Equal(2, stored.Count);

        var revoked = Assert.Single(stored, token => token.Id == previous.Id);
        Assert.Equal(Now, revoked.RevokedAt);
        Assert.Null(revoked.ConsumedAt);

        // Отзыв и вставка — в одной транзакции, письмо уходит после коммита.
        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.True(context.Tokens.AddInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
        Assert.False(context.Sender.SentInTransaction);
        Assert.Single(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_SendsLinkToVerifyEmailPageWithRawTokenAndHashedTokenIsStored()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var message = Assert.Single(context.Sender.Messages);
        Assert.Equal(context.User.Email, message.To);
        Assert.Equal(EmailVerificationMessage.SUBJECT, message.Subject);

        var match = Regex.Match(
            message.HtmlBody,
            @"href=""(http://localhost:5173/verify-email\?token=([A-Za-z0-9_-]+))""");
        Assert.True(match.Success, message.HtmlBody);

        var link = match.Groups[1].Value;
        var rawToken = match.Groups[2].Value;

        Assert.Equal(43, rawToken.Length);
        Assert.Contains(link, message.TextBody, StringComparison.Ordinal);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(EmailVerificationTokenGenerator.ComputeHash(rawToken), stored.TokenHash);
        Assert.Equal(context.User.Id, stored.UserId);
        Assert.Equal(Now, stored.IssuedAt);
        Assert.Equal(Now + EmailVerificationLifetime.TOKEN_LIFETIME, stored.ExpiresAt);

        // Публичный URL с завершающей косой чертой не даёт двойного слэша.
        Assert.DoesNotContain("//verify-email", link, StringComparison.Ordinal);

        // Пароль, id пользователя и адрес в теле письма не появляются.
        Assert.DoesNotContain(context.User.Id.ToString(), message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(context.User.Email, message.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(59)]
    public async Task Request_WithinCooldown_ReturnsConflictAndSendsNothing(int secondsAgo)
    {
        var context = new Context();
        context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("auth.emailVerification.cooldown", result.Error.Errors[0].Code);
        Assert.Single(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
        Assert.Equal(0, context.Tokens.AddCallCount);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    public async Task Request_AtOrAfterCooldown_SendsAgainAndRevokesPrevious(int secondsAgo)
    {
        var context = new Context();
        context.SeedToken(Now.AddSeconds(-secondsAgo));

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Single(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_ForAlreadyVerifiedUser_DoesNothing()
    {
        var context = new Context();
        var verified = context.User.MarkEmailAsVerified();
        Assert.True(verified.IsSuccess);

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WhenEmailIsDisabled_ReturnsUnavailable()
    {
        var context = new Context(emailEnabled: false);

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("auth.emailVerification.unavailable", result.Error.Errors[0].Code);
        Assert.Empty(context.StoredTokens);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WhenPublicUrlIsEmpty_ReturnsUnavailable()
    {
        var context = new Context(publicUrl: null);

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.unavailable", result.Error.Errors[0].Code);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WhenSenderFails_ReturnsSendFailedAndKeepsStoredToken()
    {
        var context = new Context();
        context.Sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("smtp.unavailable", "SMTP is unreachable.")));

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FAILURE, result.Error.Errors[0].Type);
        Assert.Equal("auth.emailVerification.sendFailed", result.Error.Errors[0].Code);

        // Отправка идёт после коммита: токен остался, чтобы повторная отправка была
        // осмысленной; откатывать его из-за сбоя транспорта бессмысленно.
        Assert.Single(context.StoredTokens);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Request_WhenUserDoesNotExist_ReturnsNotFound()
    {
        var context = new Context();

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_WhenInsertFails_RollsBackRevokedToken()
    {
        var context = new Context();
        var previous = context.SeedToken(Now.AddMinutes(-5));
        context.Tokens.AddBehavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("emailVerification.add.failed", "Insert failed.")));

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, context.UnitOfWork.RollbackCount);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(previous.Id, stored.Id);
        Assert.Null(stored.RevokedAt);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Request_CountsCooldownFromIssuedAtNotCreatedAt()
    {
        var context = new Context();

        // Строка создана только что системными часами, но бизнес-время выдачи — два часа
        // назад: кулдаун уже прошёл, повторная отправка разрешена.
        var token = EmailVerificationToken.Create(
            context.User.Id,
            EmailVerificationTokenGenerator.ComputeHash(EmailVerificationTokenGenerator.Generate()),
            Now.AddHours(-2),
            Now.AddHours(22),
            Now.AddHours(-2)).Value;
        context.Tokens.AddRaw(token);

        var result = await context.Requester.Handle(
            new RequestEmailVerificationCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Sender.Messages);
    }

    [Fact]
    public async Task Confirm_WithValidToken_VerifiesUserAndConsumesTokenInsideTransaction()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        var userUpdateInTransaction = false;
        context.Users.UpdateBehavior = _ =>
        {
            userUpdateInTransaction = context.UnitOfWork.IsInTransaction;
            return UnitResult.Success<Failure>();
        };

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(context.StoredUser.IsEmailVerified);

        var stored = Assert.Single(context.StoredTokens);
        Assert.Equal(token.Id, stored.Id);
        Assert.Equal(Now, stored.ConsumedAt);
        Assert.Null(stored.RevokedAt);

        Assert.True(userUpdateInTransaction);
        Assert.True(context.Tokens.UpdateInTransaction);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Confirm_WithUnknownToken_ReturnsInvalid()
    {
        var context = new Context();

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(EmailVerificationTokenGenerator.Generate()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
        Assert.False(context.StoredUser.IsEmailVerified);
    }

    [Fact]
    public async Task Confirm_WithExpiredToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddDays(-2), TimeSpan.FromHours(24));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
        Assert.False(context.StoredUser.IsEmailVerified);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task Confirm_WithUsedToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Consume(Now.AddMinutes(-1)).IsSuccess);
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
        Assert.False(context.StoredUser.IsEmailVerified);
    }

    [Fact]
    public async Task Confirm_WithRevokedToken_ReturnsInvalid()
    {
        var context = new Context();
        var (rawToken, token) = context.SeedRawToken(Now.AddMinutes(-5));
        Assert.True(token.Revoke(Now.AddMinutes(-1)).IsSuccess);
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Confirm_WhenUserIsAlreadyVerified_ReturnsSuccessWithoutConsumingToken()
    {
        var context = new Context();
        Assert.True(context.User.MarkEmailAsVerified().IsSuccess);
        var (rawToken, _) = context.SeedRawToken(Now.AddDays(-2), TimeSpan.FromHours(24));

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(context.StoredTokens.Single().ConsumedAt);
        Assert.Equal(0, context.Tokens.UpdateCallCount);
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
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Tokens.GetByHashCallCount);
        Assert.Equal(0, context.Users.GetByIdCallCount);
    }

    [Fact]
    public async Task Confirm_WithTokenOfMissingUser_ReturnsInvalid()
    {
        var context = new Context();
        var rawToken = EmailVerificationTokenGenerator.Generate();
        var token = EmailVerificationToken.Create(
            Guid.CreateVersion7(),
            EmailVerificationTokenGenerator.ComputeHash(rawToken),
            Now,
            Now.AddHours(24),
            Now).Value;
        context.Tokens.AddRaw(token);

        var result = await context.Confirmer.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.emailVerification.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Confirm_WhenConcurrentVerificationWon_ReturnsSuccessInsteadOfConflict()
    {
        var context = new Context();
        var (rawToken, _) = context.SeedRawToken(Now.AddMinutes(-5));

        // Второй confirm проиграл optimistic concurrency, но параллельный запрос уже
        // подтвердил адрес: для клиента это успех. Фейковая транзакция без участников
        // не откатывает пользователя — как уже зафиксированный COMMIT победителя.
        var handler = new ConfirmEmailVerificationHandler(
            context.Users,
            context.Tokens,
            new FakeUnitOfWork(),
            context.Clock,
            NullLogger<ConfirmEmailVerificationHandler>.Instance);

        context.Users.UpdateBehavior = user =>
        {
            user.MarkEmailAsVerified();
            return UnitResult.Failure(new Failure(Error.Conflict(
                ApiErrorCodes.User.UpdateConcurrencyConflict,
                "The user was modified by another request.")));
        };

        var result = await handler.Handle(
            new ConfirmEmailVerificationCommand(rawToken),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(context.StoredUser.IsEmailVerified);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyRowsOlderThanRetention()
    {
        var context = new Context();

        // Истёк 40 дней назад — за границей 30-дневного хранения, удаляется.
        context.SeedToken(Now.AddDays(-40), TimeSpan.FromDays(1));

        // Использован 35 дней назад, но срок ещё не истёк — удаляется по consumed_at.
        var consumedLongAgo = EmailVerificationToken.Create(
            context.User.Id,
            EmailVerificationTokenGenerator.ComputeHash(EmailVerificationTokenGenerator.Generate()),
            Now.AddDays(-40),
            Now.AddDays(20),
            Now.AddDays(-40)).Value;
        Assert.True(consumedLongAgo.Consume(Now.AddDays(-35)).IsSuccess);
        context.Tokens.AddRaw(consumedLongAgo);

        // Недавно истёк — остаётся.
        context.SeedToken(Now.AddDays(-2), TimeSpan.FromHours(1));

        // Активный — остаётся; по нему считается кулдаун.
        context.SeedToken(Now, TimeSpan.FromHours(24));

        var handler = new CleanupEmailVerificationTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupEmailVerificationTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupEmailVerificationTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Deleted);
        Assert.Equal(2, context.StoredTokens.Count);
        Assert.Contains(context.StoredTokens, token => token.ConsumedAt is null && token.RevokedAt is null);
    }

    [Fact]
    public async Task Cleanup_WithoutStaleRows_DeletesNothing()
    {
        var context = new Context();
        context.SeedToken(Now, TimeSpan.FromHours(24));

        var handler = new CleanupEmailVerificationTokensHandler(
            context.Tokens,
            context.Clock,
            NullLogger<CleanupEmailVerificationTokensHandler>.Instance);

        var result = await handler.Handle(
            new CleanupEmailVerificationTokensCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Deleted);
        Assert.Single(context.StoredTokens);
    }

    /// <summary>
    /// Общий сценарий: один пользователь, фейковые репозитории и часы.
    /// </summary>
    private sealed class Context
    {
        public Context(bool emailEnabled = true, string? publicUrl = "http://localhost:5173/")
        {
            User = User.Create(Email, "hash", "Иван", "Петров").Value;
            Users.Add(User);

            Tokens = new FakeEmailVerificationTokenRepository();
            UnitOfWork = new FakeUnitOfWork(Users, Tokens);
            Tokens.IsInTransaction = () => UnitOfWork.IsInTransaction;
            Sender.IsInTransaction = () => UnitOfWork.IsInTransaction;

            var emailOptions = new EmailOptions
            {
                Enabled = emailEnabled,
                Host = "localhost",
                Port = 1025,
                UseTls = false,
                FromAddress = "no-reply@barbersearch.test"
            };

            Requester = new RequestEmailVerificationHandler(
                Users,
                Tokens,
                UnitOfWork,
                Sender,
                emailOptions,
                new AppOptions { PublicUrl = publicUrl },
                Clock,
                NullLogger<RequestEmailVerificationHandler>.Instance);

            Confirmer = new ConfirmEmailVerificationHandler(
                Users,
                Tokens,
                UnitOfWork,
                Clock,
                NullLogger<ConfirmEmailVerificationHandler>.Instance);
        }

        public const string Email = "client@example.com";

        public FakeTimeProvider Clock { get; } = new(Now);

        public FakeUserRepository Users { get; } = new();

        public FakeEmailVerificationTokenRepository Tokens { get; }

        public FakeEmailSender Sender { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; }

        public User User { get; }

        public RequestEmailVerificationHandler Requester { get; }

        public ConfirmEmailVerificationHandler Confirmer { get; }

        public User StoredUser => Users.Users.Single();

        public IReadOnlyCollection<EmailVerificationToken> StoredTokens => Tokens.Tokens;

        /// <summary>
        /// Кладёт в «таблицу» активный токен, выданный <paramref name="issuedAt"/>.
        /// </summary>
        public EmailVerificationToken SeedToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var token = EmailVerificationToken.Create(
                User.Id,
                EmailVerificationTokenGenerator.ComputeHash(EmailVerificationTokenGenerator.Generate()),
                issuedAt,
                issuedAt + (lifetime ?? TimeSpan.FromHours(24)),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return token;
        }

        /// <summary>
        /// Кладёт активный токен и возвращает открытое значение вместе со строкой.
        /// </summary>
        public (string RawToken, EmailVerificationToken Token) SeedRawToken(
            DateTimeOffset issuedAt,
            TimeSpan? lifetime = null)
        {
            var rawToken = EmailVerificationTokenGenerator.Generate();
            var token = EmailVerificationToken.Create(
                User.Id,
                EmailVerificationTokenGenerator.ComputeHash(rawToken),
                issuedAt,
                issuedAt + (lifetime ?? TimeSpan.FromHours(24)),
                issuedAt <= Now ? Now : issuedAt).Value;

            Tokens.AddRaw(token);

            return (rawToken, token);
        }
    }
}
