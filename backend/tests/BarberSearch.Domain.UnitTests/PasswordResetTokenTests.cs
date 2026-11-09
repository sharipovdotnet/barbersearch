using BarberSearch.Domain.PasswordResets;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Домен одноразового токена сброса пароля: согласованность времени, граница срока,
/// идемпотентные <c>Consume</c> и <c>Revoke</c>.
/// </summary>
/// <remarks>
/// Хеш в тестах — валидная lower-hex строка из 64 символов: домен проверяет формат, но не
/// вычисляет SHA-256 (это делает Application). Время всегда передаётся аргументом — домен
/// часов не читает (R-27, AGENT.md §7.6).
/// </remarks>
public class PasswordResetTokenTests
{
    private const string ValidHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static PasswordResetToken CreateValidToken()
        => PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            Now.AddMinutes(-5),
            Now.AddMinutes(30),
            Now).Value;

    [Fact]
    public void Create_WithValidData_StartsActive()
    {
        var userId = Guid.CreateVersion7();

        var result = PasswordResetToken.Create(
            userId,
            ValidHash,
            Now,
            Now.AddMinutes(30),
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(ValidHash, result.Value.TokenHash);
        Assert.Equal(Now, result.Value.IssuedAt);
        Assert.Equal(Now.AddMinutes(30), result.Value.ExpiresAt);
        Assert.Null(result.Value.ConsumedAt);
        Assert.Null(result.Value.RevokedAt);
        Assert.True(result.Value.IsActive(Now));
    }

    [Fact]
    public void Create_WithEmptyUserId_ReturnsValidationFailure()
    {
        var result = PasswordResetToken.Create(
            Guid.Empty,
            ValidHash,
            Now,
            Now.AddMinutes(30),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.userId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Create_WithBrokenHash_ReturnsValidationFailure(string tokenHash)
    {
        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            tokenHash,
            Now,
            Now.AddMinutes(30),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.tokenHash");
    }

    [Fact]
    public void Create_WithDefaultIssuedAt_ReturnsValidationFailure()
    {
        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            default,
            Now.AddMinutes(30),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.issuedAt");
    }

    [Fact]
    public void Create_WithDefaultNow_ReturnsValidationFailure()
    {
        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            Now,
            Now.AddMinutes(30),
            default);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.now");
    }

    [Fact]
    public void Create_WithExpiryEqualToIssuedAt_ReturnsValidationFailure()
    {
        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            Now,
            Now,
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.expiresAt");
    }

    [Fact]
    public void Create_WithExpiryBeforeIssuedAt_ReturnsValidationFailure()
    {
        var issuedAt = Now.AddMinutes(-5);

        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            issuedAt,
            issuedAt.AddMinutes(-1),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.expiresAt");
    }

    [Fact]
    public void Create_WithIssuedAtInTheFuture_ReturnsValidationFailure()
    {
        var result = PasswordResetToken.Create(
            Guid.CreateVersion7(),
            ValidHash,
            Now.AddMinutes(1),
            Now.AddMinutes(30),
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "passwordResetToken.create.issuedAt");
    }

    [Fact]
    public void Create_CollectsEveryErrorAtOnce()
    {
        var result = PasswordResetToken.Create(Guid.Empty, "broken", default, default, default);

        Assert.True(result.IsFailure);
        Assert.Equal(5, result.Error.Errors.Count);
    }

    [Fact]
    public void IsActive_AtExpiresAt_IsFalse()
    {
        var token = CreateValidToken();

        // Сравнение строгое: в момент ExpiresAt токен уже истёк.
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
        var result = token.Consume(Now.AddMinutes(5));

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
        Assert.Equal("passwordResetToken.consume.invalid", result.Error.Errors[0].Code);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Consume_WithExpiredToken_ReturnsExpired()
    {
        var token = CreateValidToken();

        var result = token.Consume(token.ExpiresAt);

        Assert.True(result.IsFailure);
        Assert.Equal("passwordResetToken.consume.expired", result.Error.Errors[0].Code);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Consume_OnRevokedAndExpiredToken_ReportsRevokedFirst()
    {
        var token = CreateValidToken();
        Assert.True(token.Revoke(Now).IsSuccess);

        var result = token.Consume(token.ExpiresAt.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("passwordResetToken.consume.invalid", result.Error.Errors[0].Code);
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
        var result = token.Revoke(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void Revoke_WithExpiredButNotRevokedToken_SetsRevokedAt()
    {
        var token = CreateValidToken();

        // Переотправка отзывает старую строку, не разбираясь со сроками.
        var result = token.Revoke(token.ExpiresAt.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public void Revoke_WithConsumedToken_IsIdempotentAndKeepsConsumedAt()
    {
        var token = CreateValidToken();
        Assert.True(token.Consume(Now).IsSuccess);

        var result = token.Revoke(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Null(token.RevokedAt);
        Assert.Equal(Now, token.ConsumedAt);
    }
}
