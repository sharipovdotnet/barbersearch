using BarberSearch.Domain.EmailChanges;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Домен одноразового токена смены email: запрошенный новый адрес, согласованность времени,
/// граница срока и идемпотентные <c>Consume</c> и <c>Revoke</c>.
/// </summary>
/// <remarks>
/// Хеш в тестах — валидная lower-hex строка из 64 символов: домен проверяет формат, но не
/// вычисляет SHA-256 (это делает Application). Время всегда передаётся аргументом — домен
/// часов не читает (R-27, AGENT.md §7.6). Открытый токен в тестах нигде не сохраняется.
/// </remarks>
public class EmailChangeTokenTests
{
    private const string ValidHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string ValidNewEmail = "new@example.com";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static EmailChangeToken CreateValidToken()
        => EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            ValidHash,
            Now.AddMinutes(-5),
            Now.AddHours(24),
            Now).Value;

    [Fact]
    public void Create_WithValidData_StartsActive()
    {
        var userId = Guid.CreateVersion7();

        var result = EmailChangeToken.Create(
            userId,
            ValidNewEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(ValidNewEmail, result.Value.NewEmail);
        Assert.Equal(ValidHash, result.Value.TokenHash);
        Assert.Equal(Now, result.Value.IssuedAt);
        Assert.Equal(Now.AddHours(24), result.Value.ExpiresAt);
        Assert.Null(result.Value.ConsumedAt);
        Assert.Null(result.Value.RevokedAt);
        Assert.True(result.Value.IsActive(Now));
    }

    [Fact]
    public void Create_NormalizesNewEmail()
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            "  New@Example.COM  ",
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidNewEmail, result.Value.NewEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@b@c")]
    [InlineData("Ivan <ivan@example.com>")]
    public void Create_WithBrokenNewEmail_ReturnsValidationFailure(string newEmail)
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            newEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal("emailChangeToken.create.newEmail", error.Code);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("newEmail", error.InvalidField);
    }

    [Fact]
    public void Create_AtMaximumEmailLength_IsAccepted()
    {
        // 254 символа — предел RFC 5321 и колонки varchar(254).
        var newEmail = new string('a', 242) + "@example.com";

        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            newEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmail, result.Value.NewEmail);
    }

    [Fact]
    public void Create_AboveMaximumEmailLength_ReturnsValidationFailure()
    {
        var newEmail = new string('a', 243) + "@example.com";

        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            newEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.newEmail");
    }

    [Fact]
    public void Create_WithEmptyUserId_ReturnsValidationFailure()
    {
        var result = EmailChangeToken.Create(
            Guid.Empty,
            ValidNewEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.userId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Create_WithBrokenHash_ReturnsValidationFailure(string tokenHash)
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            tokenHash,
            Now,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.tokenHash");
    }

    [Fact]
    public void Create_WithDefaultIssuedAt_ReturnsValidationFailure()
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            ValidHash,
            default,
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.issuedAt");
    }

    [Fact]
    public void Create_WithIssuedAtInTheFuture_ReturnsValidationFailure()
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            ValidHash,
            Now.AddMinutes(1),
            Now.AddHours(24),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.issuedAt");
    }

    [Fact]
    public void Create_WithDefaultNow_ReturnsValidationFailure()
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            ValidHash,
            Now,
            Now.AddHours(24),
            default);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.now");
    }

    [Fact]
    public void Create_WithExpiryNotAfterIssuedAt_ReturnsValidationFailure()
    {
        var result = EmailChangeToken.Create(
            Guid.CreateVersion7(),
            ValidNewEmail,
            ValidHash,
            Now,
            Now,
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailChangeToken.create.expiresAt");
    }

    [Fact]
    public void Create_CollectsEveryErrorAtOnce()
    {
        var result = EmailChangeToken.Create(Guid.Empty, "", "broken", default, default, default);

        Assert.True(result.IsFailure);

        // Адрес, пользователь, хеш, issuedAt, now и expiresAt — все проверки независимы.
        Assert.Equal(6, result.Error.Errors.Count);
    }

    [Fact]
    public void IsActive_AtExpiresAt_IsFalse()
    {
        var token = CreateValidToken();

        // Сравнение строгое: в момент ExpiresAt токен уже истёк — как блокировка входа.
        Assert.False(token.IsActive(token.ExpiresAt));
        Assert.True(token.IsActive(token.ExpiresAt.AddTicks(-1)));
    }

    [Fact]
    public void IsActive_AfterConsumeOrRevoke_IsFalse()
    {
        var consumed = CreateValidToken();
        Assert.True(consumed.Consume(Now).IsSuccess);

        var revoked = CreateValidToken();
        Assert.True(revoked.Revoke(Now).IsSuccess);

        Assert.False(consumed.IsActive(Now));
        Assert.False(revoked.IsActive(Now));
    }

    [Fact]
    public void Consume_WithActiveToken_SetsConsumedAt()
    {
        var token = CreateValidToken();

        var result = token.Consume(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, token.ConsumedAt);
        Assert.Null(token.RevokedAt);
    }

    [Fact]
    public void Consume_Repeated_IsIdempotentAndKeepsFirstTimestamp()
    {
        var token = CreateValidToken();

        Assert.True(token.Consume(Now).IsSuccess);
        var result = token.Consume(Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, token.ConsumedAt);
    }

    [Fact]
    public void Consume_WithRevokedToken_ReturnsInvalid()
    {
        var token = CreateValidToken();
        Assert.True(token.Revoke(Now).IsSuccess);

        var result = token.Consume(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("emailChangeToken.consume.invalid", result.Error.Errors[0].Code);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Consume_WithExpiredToken_ReturnsExpired()
    {
        var token = CreateValidToken();

        var result = token.Consume(token.ExpiresAt);

        Assert.True(result.IsFailure);
        Assert.Equal("emailChangeToken.consume.expired", result.Error.Errors[0].Code);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Consume_OnRevokedAndExpiredToken_ReportsRevokedFirst()
    {
        var token = CreateValidToken();
        Assert.True(token.Revoke(Now).IsSuccess);

        var result = token.Consume(token.ExpiresAt.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("emailChangeToken.consume.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Revoke_WithActiveToken_SetsRevokedAt()
    {
        var token = CreateValidToken();

        var result = token.Revoke(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, token.RevokedAt);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Revoke_Repeated_IsIdempotentAndKeepsFirstTimestamp()
    {
        var token = CreateValidToken();

        Assert.True(token.Revoke(Now).IsSuccess);
        var result = token.Revoke(Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void Revoke_WithExpiredButNotRevokedToken_SetsRevokedAt()
    {
        var token = CreateValidToken();

        // Новая заявка гасит прежнюю строку, не разбираясь со сроками: истёкший,
        // но не подтверждённый токен тоже должен уступить новому.
        var result = token.Revoke(token.ExpiresAt.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public void Revoke_WithConsumedToken_IsIdempotentAndKeepsConsumedAt()
    {
        var token = CreateValidToken();
        Assert.True(token.Consume(Now).IsSuccess);

        var result = token.Revoke(Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Null(token.RevokedAt);
        Assert.Equal(Now, token.ConsumedAt);
    }
}
