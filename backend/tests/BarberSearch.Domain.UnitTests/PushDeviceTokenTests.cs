using BarberSearch.Domain.Push;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Токен устройства: проверка входа и передача владения при повторной регистрации.
/// </summary>
/// <remarks>
/// Токен — непрозрачная строка транспорта, поэтому домен проверяет только непустоту, предел
/// колонки и определённость платформы. «Сейчас» приходит аргументом: домен бизнес-часы
/// не читает (AGENT.md §7.6).
/// </remarks>
public class PushDeviceTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void Register_WithEmptyToken_Fails()
    {
        var result = PushDeviceToken.Register(UserId, string.Empty, PushPlatform.Android, Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "pushDeviceToken.token.invalid" && error.InvalidField == "token");
    }

    [Fact]
    public void Register_WithWhitespaceToken_Fails()
    {
        var result = PushDeviceToken.Register(UserId, "   ", PushPlatform.Android, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("pushDeviceToken.token.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Register_WithTokenLongerThanTheColumn_Fails()
    {
        var token = new string('a', 513);

        var result = PushDeviceToken.Register(UserId, token, PushPlatform.Android, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("pushDeviceToken.token.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Register_WithTokenAtTheColumnLimit_Succeeds()
    {
        var token = new string('a', 512);

        var result = PushDeviceToken.Register(UserId, token, PushPlatform.Android, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(token, result.Value.Token);
    }

    [Fact]
    public void Register_WithUnknownPlatform_Fails()
    {
        var result = PushDeviceToken.Register(UserId, "token", (PushPlatform)99, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("pushDeviceToken.platform.unknown", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Register_TrimsTheTokenAndStoresTheSeenAt()
    {
        var result = PushDeviceToken.Register(
            UserId,
            "  token-with-newline\r\n",
            PushPlatform.Android,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("token-with-newline", result.Value.Token);
        Assert.Equal(UserId, result.Value.UserId);
        Assert.Equal(PushPlatform.Android, result.Value.Platform);
        Assert.Equal(Now, result.Value.LastSeenAt);
    }

    [Fact]
    public void Touch_WithLaterMoment_MovesLastSeenAt()
    {
        var token = Create(Now);

        var result = token.Touch(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(5), token.LastSeenAt);
    }

    [Fact]
    public void Touch_WithEarlierMoment_DoesNotMoveLastSeenAtBack()
    {
        var token = Create(Now);
        Assert.True(token.Touch(Now.AddMinutes(5)).IsSuccess);

        var result = token.Touch(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(5), token.LastSeenAt);
    }

    [Fact]
    public void TransferTo_MovesTheTokenToAnotherOwner()
    {
        var token = Create(Now);
        var newOwner = Guid.CreateVersion7();

        var result = token.TransferTo(newOwner, Now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(newOwner, token.UserId);
        Assert.Equal(Now.AddDays(1), token.LastSeenAt);
    }

    private static PushDeviceToken Create(DateTimeOffset seenAt)
        => PushDeviceToken.Register(UserId, "device-token", PushPlatform.Android, seenAt).Value;
}
