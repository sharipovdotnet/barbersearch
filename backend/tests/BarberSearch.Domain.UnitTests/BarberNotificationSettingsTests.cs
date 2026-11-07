using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Настройки напоминаний: значения по умолчанию, границы интервала напоминания и времени
/// дайджеста, привязка Telegram и одноразовый код.
/// </summary>
public class BarberNotificationSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public void CreateDefault_SetsDocumentedDefaults()
    {
        var result = BarberNotificationSettings.CreateDefault(Guid.CreateVersion7());

        Assert.True(result.IsSuccess);

        var settings = result.Value;

        Assert.True(settings.IsEnabled);
        Assert.True(settings.DigestEnabled);
        Assert.Equal(new TimeOnly(8, 0), settings.DigestTime);
        Assert.Equal(Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES, settings.LeadMinutes);
        Assert.False(settings.IsTelegramLinked);
        Assert.Null(settings.TelegramChatId);
        Assert.Null(settings.TelegramLinkedAt);
    }

    [Fact]
    public void CreateDefault_WithoutOwner_ReturnsValidationFailure()
    {
        var result = BarberNotificationSettings.CreateDefault(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("notificationSettings.create.userId", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void Update_WithAllowedLeadMinutes_Succeeds(int leadMinutes)
    {
        var settings = CreateSettings();

        var result = settings.Update(true, true, new TimeOnly(9, 0), leadMinutes);

        Assert.True(result.IsSuccess);
        Assert.Equal(leadMinutes, settings.LeadMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(61)]
    public void Update_WithForbiddenLeadMinutes_ReturnsLeadMinutesCode(int leadMinutes)
    {
        var settings = CreateSettings();

        var result = settings.Update(true, true, new TimeOnly(9, 0), leadMinutes);

        Assert.True(result.IsFailure);
        Assert.Equal(
            BarberNotificationSettings.LEAD_MINUTES_CODE,
            result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES, settings.LeadMinutes);
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(8, 0)]
    [InlineData(8, 15)]
    [InlineData(12, 0)]
    public void Update_WithAllowedDigestTime_Succeeds(int hour, int minute)
    {
        var settings = CreateSettings();
        var digestTime = new TimeOnly(hour, minute);

        var result = settings.Update(true, true, digestTime, 30);

        Assert.True(result.IsSuccess);
        Assert.Equal(digestTime, settings.DigestTime);
    }

    [Theory]
    [InlineData(5, 45)]
    [InlineData(12, 15)]
    [InlineData(8, 7)]
    public void Update_WithForbiddenDigestTime_ReturnsDigestTimeCode(int hour, int minute)
    {
        var settings = CreateSettings();

        var result = settings.Update(true, true, new TimeOnly(hour, minute), 30);

        Assert.True(result.IsFailure);
        Assert.Equal(
            BarberNotificationSettings.DIGEST_TIME_CODE,
            result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public void Update_WithSecondsInDigestTime_IsRejected()
    {
        var settings = CreateSettings();

        var result = settings.Update(true, true, new TimeOnly(8, 0, 30), 30);

        Assert.True(result.IsFailure);
        Assert.Equal(
            BarberNotificationSettings.DIGEST_TIME_CODE,
            result.Error.Errors[0].Code);
    }

    [Fact]
    public void Update_CollectsBothErrorsAtOnce()
    {
        var settings = CreateSettings();

        var result = settings.Update(true, true, new TimeOnly(13, 0), 10);

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Error.Errors.Count);
    }

    [Fact]
    public void Update_ChangesFlags()
    {
        var settings = CreateSettings();

        Assert.True(settings.Update(false, false, new TimeOnly(10, 0), 60).IsSuccess);

        Assert.False(settings.IsEnabled);
        Assert.False(settings.DigestEnabled);
        Assert.Equal(new TimeOnly(10, 0), settings.DigestTime);
        Assert.Equal(60, settings.LeadMinutes);
    }

    [Fact]
    public void LinkTelegram_SetsChatAndLinkedAt()
    {
        var settings = CreateSettings();

        var result = settings.LinkTelegram(123456, Now);

        Assert.True(result.IsSuccess);
        Assert.True(settings.IsTelegramLinked);
        Assert.Equal(123456, settings.TelegramChatId);
        Assert.Equal(Now, settings.TelegramLinkedAt);
    }

    [Fact]
    public void LinkTelegram_WithZeroChat_ReturnsValidationFailure()
    {
        var settings = CreateSettings();

        var result = settings.LinkTelegram(0, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("notificationSettings.linkTelegram.chatId", result.Error.Errors[0].Code);
        Assert.False(settings.IsTelegramLinked);
    }

    [Fact]
    public void LinkTelegram_ClearsTheUsedCode()
    {
        var settings = CreateSettings();
        Assert.True(settings.IssueTelegramCode(ValidHash, Now.AddMinutes(10)).IsSuccess);

        Assert.True(settings.LinkTelegram(42, Now).IsSuccess);

        Assert.Null(settings.TelegramLinkCodeHash);
        Assert.Null(settings.TelegramLinkCodeExpiresAt);
    }

    [Fact]
    public void UnlinkTelegram_ClearsEverythingAndIsIdempotent()
    {
        var settings = CreateSettings();
        Assert.True(settings.LinkTelegram(42, Now).IsSuccess);

        var first = settings.UnlinkTelegram();
        var second = settings.UnlinkTelegram();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(settings.IsTelegramLinked);
        Assert.Null(settings.TelegramChatId);
        Assert.Null(settings.TelegramLinkedAt);
    }

    [Fact]
    public void IssueTelegramCode_StoresHashAndExpiry()
    {
        var settings = CreateSettings();
        var expiresAt = Now.AddMinutes(Constants.TELEGRAM_LINK_CODE_LIFETIME_MINUTES);

        var result = settings.IssueTelegramCode(ValidHash, expiresAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidHash, settings.TelegramLinkCodeHash);
        Assert.Equal(expiresAt, settings.TelegramLinkCodeExpiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void IssueTelegramCode_WithInvalidHash_ReturnsHashCode(string hash)
    {
        var settings = CreateSettings();

        var result = settings.IssueTelegramCode(hash, Now.AddMinutes(10));

        Assert.True(result.IsFailure);
        Assert.Equal("notificationSettings.issueTelegramCode.hash", result.Error.Errors[0].Code);
        Assert.Null(settings.TelegramLinkCodeHash);
    }

    [Fact]
    public void IssueTelegramCode_WithoutExpiry_ReturnsExpiresAtCode()
    {
        var settings = CreateSettings();

        var result = settings.IssueTelegramCode(ValidHash, default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "notificationSettings.issueTelegramCode.expiresAt",
            result.Error.Errors[0].Code);
    }

    [Fact]
    public void ClearTelegramCode_KeepsTheLinkAndIsIdempotent()
    {
        var settings = CreateSettings();
        Assert.True(settings.LinkTelegram(42, Now).IsSuccess);
        Assert.True(settings.IssueTelegramCode(ValidHash, Now.AddMinutes(10)).IsSuccess);

        var first = settings.ClearTelegramCode();
        var second = settings.ClearTelegramCode();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Null(settings.TelegramLinkCodeHash);
        Assert.Null(settings.TelegramLinkCodeExpiresAt);
        Assert.True(settings.IsTelegramLinked);
        Assert.Equal(42, settings.TelegramChatId);
    }

    private static BarberNotificationSettings CreateSettings()
        => BarberNotificationSettings.CreateDefault(Guid.CreateVersion7()).Value;
}
