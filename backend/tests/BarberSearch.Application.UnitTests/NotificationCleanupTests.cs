using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.CleanupOldNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Суточная уборка инбокса: граница ретенции считается от <see cref="TimeProvider"/> и
/// конфигурации, а не от системного времени.
/// </summary>
public class NotificationCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cleanup_UsesDefaultRetentionFromTheClock()
    {
        var notifications = new FakeBarberNotificationRepository();

        var handler = new CleanupOldNotificationsHandler(
            notifications,
            new RemindersOptions(),
            new FakeTimeProvider(Now),
            NullLogger<CleanupOldNotificationsHandler>.Instance);

        var result = await handler.Handle(
            new CleanupOldNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Now.AddDays(-Constants.NOTIFICATION_RETENTION_DAYS),
            notifications.LastDeleteCutoff);
    }

    [Fact]
    public async Task Cleanup_UsesConfiguredRetention()
    {
        var notifications = new FakeBarberNotificationRepository();

        var handler = new CleanupOldNotificationsHandler(
            notifications,
            new RemindersOptions { RetentionDays = 30 },
            new FakeTimeProvider(Now),
            NullLogger<CleanupOldNotificationsHandler>.Instance);

        await handler.Handle(new CleanupOldNotificationsCommand(), CancellationToken.None);

        Assert.Equal(Now.AddDays(-30), notifications.LastDeleteCutoff);
    }

    [Fact]
    public async Task Cleanup_WithInvalidRetention_FallsBackToTheDefault()
    {
        var notifications = new FakeBarberNotificationRepository();

        var handler = new CleanupOldNotificationsHandler(
            notifications,
            new RemindersOptions { RetentionDays = 0 },
            new FakeTimeProvider(Now),
            NullLogger<CleanupOldNotificationsHandler>.Instance);

        await handler.Handle(new CleanupOldNotificationsCommand(), CancellationToken.None);

        Assert.Equal(
            Now.AddDays(-Constants.NOTIFICATION_RETENTION_DAYS),
            notifications.LastDeleteCutoff);
    }
}
