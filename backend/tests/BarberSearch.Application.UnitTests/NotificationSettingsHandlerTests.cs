using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.CreateTelegramLink;
using BarberSearch.Application.Notifications.GetNotificationSettings;
using BarberSearch.Application.Notifications.UnlinkTelegram;
using BarberSearch.Application.Notifications.UpdateNotificationSettings;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Настройки напоминаний: значения по умолчанию при первом чтении, валидация правки и
/// привязка Telegram (код, хеш, срок, конфликты).
/// </summary>
public class NotificationSettingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    private const string BotUsername = "barbersearch_bot";

    private static readonly FakeTelegramBot ConfiguredBot =
        new(botUsername: BotUsername);

    [Fact]
    public async Task Get_CreatesDefaultsOnTheFirstRead()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new GetNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(new GetNotificationSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, settings.AddCalls);

        var response = result.Value;
        Assert.True(response.IsEnabled);
        Assert.True(response.DigestEnabled);
        Assert.Equal("08:00", response.DigestTime);
        Assert.Equal(Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES, response.LeadMinutes);
        Assert.False(response.TelegramLinked);
        Assert.Null(response.TelegramLinkedAt);
        Assert.True(response.BotConfigured);
    }

    [Fact]
    public async Task Get_SecondRead_DoesNotCreateAnotherRow()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new GetNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        await handler.Handle(new GetNotificationSettingsQuery(), CancellationToken.None);
        await handler.Handle(new GetNotificationSettingsQuery(), CancellationToken.None);

        Assert.Equal(1, settings.AddCalls);
        Assert.Single(settings.All);
    }

    [Fact]
    public async Task Get_WithoutConfiguredBot_ReportsBotConfiguredFalse()
    {
        var userId = Guid.CreateVersion7();

        var handler = new GetNotificationSettingsHandler(
            new FakeBarberNotificationSettingsRepository(),
            new FakeCurrentUser(userId),
            new FakeTelegramBot(isConfigured: false));

        var result = await handler.Handle(new GetNotificationSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.BotConfigured);
    }

    [Fact]
    public async Task Get_WithLinkedChat_ReportsLinkButNoChatId()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();
        var stored = BarberNotificationSettings.CreateDefault(userId).Value;
        Assert.True(stored.LinkTelegram(4242, Now).IsSuccess);
        settings.Seed(stored);

        var handler = new GetNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(new GetNotificationSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.TelegramLinked);
        Assert.Equal(Now, result.Value.TelegramLinkedAt);
    }

    [Fact]
    public async Task Update_SavesValidValues()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new UpdateNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(
            new UpdateNotificationSettingsCommand(
                new UpdateNotificationSettingsRequest(false, true, "09:30", 60)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);
        Assert.True(result.Value.DigestEnabled);
        Assert.Equal("09:30", result.Value.DigestTime);
        Assert.Equal(60, result.Value.LeadMinutes);

        var stored = Assert.Single(settings.All);
        Assert.Equal(new TimeOnly(9, 30), stored.DigestTime);
        Assert.Equal(60, stored.LeadMinutes);
    }

    [Fact]
    public async Task Update_OnMissingSettings_CreatesThemWithTheSentValues()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new UpdateNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(
            new UpdateNotificationSettingsCommand(
                new UpdateNotificationSettingsRequest(true, false, "10:00", 15)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, settings.AddCalls);
        Assert.Equal("10:00", result.Value.DigestTime);
        Assert.Equal(15, result.Value.LeadMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(120)]
    public async Task Update_WithForbiddenLeadMinutes_ReturnsPublicCode(int leadMinutes)
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new UpdateNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(
            new UpdateNotificationSettingsCommand(
                new UpdateNotificationSettingsRequest(true, true, "08:00", leadMinutes)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            BarberNotificationSettings.LEAD_MINUTES_CODE,
            result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Theory]
    [InlineData("05:45")]
    [InlineData("12:15")]
    [InlineData("8:00")]
    [InlineData("08:00:00")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Update_WithInvalidDigestTime_ReturnsPublicCode(string? digestTime)
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new UpdateNotificationSettingsHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot);

        var result = await handler.Handle(
            new UpdateNotificationSettingsCommand(
                new UpdateNotificationSettingsRequest(true, true, digestTime, 30)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            BarberNotificationSettings.DIGEST_TIME_CODE,
            result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task CreateTelegramLink_IssuesBase32CodeAndDeepLink()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();

        var handler = new CreateTelegramLinkHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot,
            new FakeTimeProvider(Now));

        var result = await handler.Handle(new CreateTelegramLinkCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var response = result.Value;
        Assert.Equal(Constants.TELEGRAM_LINK_CODE_LENGTH, response.Code.Length);
        Assert.All(
            response.Code,
            symbol => Assert.Contains(symbol, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        Assert.Equal($"https://t.me/{BotUsername}?start={response.Code}", response.DeepLink);
        Assert.Equal(Now.AddMinutes(Constants.TELEGRAM_LINK_CODE_LIFETIME_MINUTES), response.ExpiresAt);

        var stored = Assert.Single(settings.All);
        Assert.Equal(TelegramLinkCode.Hash(response.Code), stored.TelegramLinkCodeHash);
        Assert.Equal(response.ExpiresAt, stored.TelegramLinkCodeExpiresAt);
        Assert.False(stored.IsTelegramLinked);
    }

    [Fact]
    public async Task CreateTelegramLink_WhenChatAlreadyLinked_ReturnsConflict()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();
        var stored = BarberNotificationSettings.CreateDefault(userId).Value;
        Assert.True(stored.LinkTelegram(4242, Now).IsSuccess);
        settings.Seed(stored);

        var handler = new CreateTelegramLinkHandler(
            settings,
            new FakeCurrentUser(userId),
            ConfiguredBot,
            new FakeTimeProvider(Now));

        var result = await handler.Handle(new CreateTelegramLinkCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Notification.TelegramAlreadyLinked, result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task CreateTelegramLink_WithoutConfiguredBot_ReturnsConflict()
    {
        var userId = Guid.CreateVersion7();

        var handler = new CreateTelegramLinkHandler(
            new FakeBarberNotificationSettingsRepository(),
            new FakeCurrentUser(userId),
            new FakeTelegramBot(isConfigured: false),
            new FakeTimeProvider(Now));

        var result = await handler.Handle(new CreateTelegramLinkCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Notification.TelegramNotConfigured,
            result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task CreateTelegramLink_Anonymous_ReturnsUnauthorized()
    {
        var handler = new CreateTelegramLinkHandler(
            new FakeBarberNotificationSettingsRepository(),
            FakeCurrentUser.Anonymous(),
            ConfiguredBot,
            new FakeTimeProvider(Now));

        var result = await handler.Handle(new CreateTelegramLinkCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Auth.Unauthorized, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Unlink_ClearsTheChatAndIsIdempotent()
    {
        var userId = Guid.CreateVersion7();
        var settings = new FakeBarberNotificationSettingsRepository();
        var stored = BarberNotificationSettings.CreateDefault(userId).Value;
        Assert.True(stored.LinkTelegram(4242, Now).IsSuccess);
        settings.Seed(stored);

        var handler = new UnlinkTelegramHandler(settings, new FakeCurrentUser(userId));

        var first = await handler.Handle(new UnlinkTelegramCommand(), CancellationToken.None);
        var second = await handler.Handle(new UnlinkTelegramCommand(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Null(stored.TelegramChatId);
        Assert.False(stored.IsTelegramLinked);
    }

    [Fact]
    public async Task Unlink_WithoutSettings_IsSuccess()
    {
        var userId = Guid.CreateVersion7();
        var handler = new UnlinkTelegramHandler(
            new FakeBarberNotificationSettingsRepository(),
            new FakeCurrentUser(userId));

        var result = await handler.Handle(new UnlinkTelegramCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
