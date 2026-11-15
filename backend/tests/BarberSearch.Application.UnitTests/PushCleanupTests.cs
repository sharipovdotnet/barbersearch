using BarberSearch.Application.Push;
using BarberSearch.Application.Push.CleanupPushNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Суточная уборка push-данных: две разные ретенции и защита от нулевого значения.
/// </summary>
/// <remarks>
/// Строка очереди живёт 30 дней, токен устройства — 180 дней без повторной регистрации.
/// Обе границы считаются от <see cref="TimeProvider"/>, а некорректная конфигурация
/// заменяется значением по умолчанию, а не удаляет всё подряд.
/// </remarks>
public class PushCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Cleanup_DeletesByBothRetentionsFromTheClock()
    {
        var context = CreateContext(new PushOptions
        {
            RetentionDays = 30,
            DeviceTokenRetentionDays = 180
        });

        var result = await context.Handler.Handle(
            new CleanupPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(-30), context.Notifications.LastDeleteCutoff);
        Assert.Equal(Now.AddDays(-180), context.Tokens.LastDeleteStaleCutoff);
    }

    [Fact]
    public async Task Cleanup_RemovesOnlyStaleDeviceTokens()
    {
        var context = CreateContext(new PushOptions
        {
            RetentionDays = 30,
            DeviceTokenRetentionDays = 180
        });

        context.Tokens.Seed(CreateToken("stale", Now.AddDays(-181)));
        context.Tokens.Seed(CreateToken("fresh", Now.AddDays(-179)));

        var result = await context.Handler.Handle(
            new CleanupPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.DeletedNotifications);
        Assert.Equal(1, result.Value.DeletedDeviceTokens);
        var remaining = Assert.Single(context.Tokens.Items);
        Assert.Equal("fresh", remaining.Token);
    }

    [Fact]
    public async Task Cleanup_WithNonPositiveRetention_UsesDefaults()
    {
        var context = CreateContext(new PushOptions
        {
            RetentionDays = 0,
            DeviceTokenRetentionDays = 0
        });

        await context.Handler.Handle(
            new CleanupPushNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(
            Now.AddDays(-PushOptions.DEFAULT_RETENTION_DAYS),
            context.Notifications.LastDeleteCutoff);
        Assert.Equal(
            Now.AddDays(-PushOptions.DEFAULT_DEVICE_TOKEN_RETENTION_DAYS),
            context.Tokens.LastDeleteStaleCutoff);
    }

    private static PushDeviceToken CreateToken(string token, DateTimeOffset lastSeenAt)
        => PushDeviceToken.Register(UserId, token, PushPlatform.Android, lastSeenAt).Value;

    private static Context CreateContext(PushOptions options)
    {
        var notifications = new FakePushNotificationRepository();
        var tokens = new FakePushDeviceTokenRepository();
        var clock = new FakeTimeProvider(Now);

        var handler = new CleanupPushNotificationsHandler(
            notifications,
            tokens,
            options,
            clock,
            NullLogger<CleanupPushNotificationsHandler>.Instance);

        return new Context(handler, notifications, tokens);
    }

    private sealed record Context(
        CleanupPushNotificationsHandler Handler,
        FakePushNotificationRepository Notifications,
        FakePushDeviceTokenRepository Tokens);
}
