using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.CreateDueNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Тик создания уведомлений: окно напоминания, дайджест по поясу основного салона и
/// дедупликация повторного запуска (§3.1, §9 спецификации варианта C).
/// </summary>
/// <remarks>
/// Часы стоят на 2026-11-10 03:00 UTC — это 08:00 в Екатеринбурге (UTC+5), то есть ровно
/// время дайджеста по умолчанию. Тесты двигают часы, а не ждут: без <see cref="TimeProvider"/>
/// проверка «минутой раньше» была бы невозможна.
/// </remarks>
public class NotificationCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    private const string SalonTimeZone = "Asia/Yekaterinburg";

    private static readonly DateOnly LocalDate = new(2026, 11, 10);

    [Fact]
    public async Task Tick_WithBookingInsideLeadWindow_CreatesUpcomingNotification()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30);
        var startAt = Now.AddMinutes(20);
        var booking = CreateBooking(startAt);
        context.Source.AddBooking(userId, booking);

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(BarberNotificationKind.Upcoming, notification.Kind);
        Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);
        Assert.Equal(booking.BookingId, notification.BookingId);
        Assert.Equal(startAt, notification.StartAt);
        Assert.Equal("стрижка", notification.ServiceName);
        Assert.Equal("Барбершоп на Ленина", notification.SalonName);
        Assert.Equal(SalonTimeZone, notification.SalonTimeZoneId);
        Assert.Null(notification.BookingsCount);
        Assert.Null(notification.DigestLocalDate);
    }

    [Fact]
    public async Task Tick_WithBookingExactlyAtLeadBoundary_CreatesNotification()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30);
        context.Source.AddBooking(userId, CreateBooking(Now.AddMinutes(30)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Single(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithBookingOneSecondBeyondLeadBoundary_DoesNotCreateNotification()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30);
        context.Source.AddBooking(userId, CreateBooking(Now.AddMinutes(30).AddSeconds(1)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithBookingStartingNow_DoesNotCreateNotification()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30);
        context.Source.AddBooking(userId, CreateBooking(Now));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
    }

    [Fact]
    public async Task Tick_WithLongerLead_CreatesNotificationFurtherAhead()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 60);
        context.Source.AddBooking(userId, CreateBooking(Now.AddMinutes(55)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
    }

    [Fact]
    public async Task Tick_Repeated_DoesNotCreateDuplicate()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30);
        context.Source.AddBooking(userId, CreateBooking(Now.AddMinutes(20)));

        var first = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);
        var second = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(1, first.Value.Created);
        Assert.Equal(0, second.Value.Created);
        Assert.Single(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WhenNotificationsDisabled_CreatesNothing()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, isEnabled: false);
        context.Source.AddBooking(userId, CreateBooking(Now.AddMinutes(20)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_AtDigestTime_CreatesDigestForTheLocalDay()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true);

        // 10:00 и 14:00 по Екатеринбургу — это 05:00 и 09:00 UTC тех же суток.
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);

        var digest = Assert.Single(
            context.Notifications.Items,
            item => item.Kind == BarberNotificationKind.Digest);

        Assert.Equal(2, digest.BookingsCount);
        Assert.Equal(LocalDate, digest.DigestLocalDate);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero),
            digest.StartAt);
        Assert.Equal("стрижка", digest.ServiceName);
        Assert.Equal(SalonTimeZone, digest.SalonTimeZoneId);
    }

    [Fact]
    public async Task Tick_OneMinuteBeforeDigestTime_DoesNotCreateDigest()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true, now: Now.AddMinutes(-1));
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            context.Notifications.Items,
            item => item.Kind == BarberNotificationKind.Digest);
    }

    [Fact]
    public async Task Tick_WithZeroBookings_DoesNotCreateDigest()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true);

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithDigestDisabled_DoesNotCreateDigest()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: false);
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_UsesTimeZoneOfThePrimarySalon()
    {
        var ekaterinburgUser = Guid.CreateVersion7();
        var utcUser = Guid.CreateVersion7();

        var context = CreateContext(ekaterinburgUser, leadMinutes: 30, digestEnabled: true);
        context.Settings.Seed(CreateSettings(utcUser, leadMinutes: 30));
        context.Source.SetPrimarySalonTimeZone(utcUser, "UTC");

        var bookingStart = new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);
        context.Source.AddBooking(ekaterinburgUser, CreateBooking(bookingStart));
        context.Source.AddBooking(utcUser, CreateBooking(bookingStart));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var digest = Assert.Single(
            context.Notifications.Items,
            item => item.Kind == BarberNotificationKind.Digest);

        Assert.Equal(ekaterinburgUser, digest.UserId);
        Assert.Equal(SalonTimeZone, digest.SalonTimeZoneId);
    }

    [Fact]
    public async Task Tick_WithUnknownTimeZone_SkipsTheUserWithoutFailing()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true, primaryTimeZone: "Mars/Phobos");
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
    }

    [Fact]
    public async Task Tick_WithoutPrimarySalon_SkipsTheDigest()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true, primaryTimeZone: null);
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));

        var result = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
    }

    [Fact]
    public async Task Tick_Repeated_DoesNotCreateDuplicateDigest()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, leadMinutes: 30, digestEnabled: true);
        context.Source.AddBooking(
            userId,
            CreateBooking(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero)));

        var first = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);
        var second = await context.Handler.Handle(
            new CreateDueNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(1, first.Value.Created);
        Assert.Equal(0, second.Value.Created);
        Assert.Single(context.Notifications.Items);
    }

    private static ReminderBooking CreateBooking(DateTimeOffset startAt)
        => new(
            Guid.CreateVersion7(),
            startAt,
            "стрижка",
            "Барбершоп на Ленина",
            SalonTimeZone);

    private static BarberNotificationSettings CreateSettings(
        Guid userId,
        int leadMinutes,
        bool isEnabled = true,
        bool digestEnabled = true,
        int digestHour = 8,
        int digestMinute = 0)
    {
        var settings = BarberNotificationSettings.CreateDefault(userId).Value;

        Assert.True(settings
            .Update(isEnabled, digestEnabled, new TimeOnly(digestHour, digestMinute), leadMinutes)
            .IsSuccess);

        return settings;
    }

    private static Context CreateContext(
        Guid userId,
        int leadMinutes,
        bool isEnabled = true,
        bool digestEnabled = false,
        string? primaryTimeZone = SalonTimeZone,
        DateTimeOffset? now = null)
    {
        var notifications = new FakeBarberNotificationRepository();
        var settings = new FakeBarberNotificationSettingsRepository();
        var source = new FakeReminderSourceRepository();
        var clock = new FakeTimeProvider(now ?? Now);

        settings.Seed(CreateSettings(userId, leadMinutes, isEnabled, digestEnabled));
        source.SetPrimarySalonTimeZone(userId, primaryTimeZone);

        var handler = new CreateDueNotificationsHandler(
            notifications,
            settings,
            source,
            clock,
            NullLogger<CreateDueNotificationsHandler>.Instance);

        return new Context(handler, notifications, settings, source);
    }

    private sealed record Context(
        CreateDueNotificationsHandler Handler,
        FakeBarberNotificationRepository Notifications,
        FakeBarberNotificationSettingsRepository Settings,
        FakeReminderSourceRepository Source);
}
