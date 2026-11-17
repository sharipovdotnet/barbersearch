using BarberSearch.Application.Push;
using BarberSearch.Application.Push.CreateDuePushNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Тик создания push-напоминаний: окно «через час», поздняя бронь и дедупликация.
/// </summary>
/// <remarks>
/// Часы стоят на 2026-11-15 12:00 UTC. Тесты двигают часы, а не ждут: без
/// <see cref="TimeProvider"/> проверка «секундой позже окна» была бы невозможна.
/// </remarks>
public class PushCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private const int LeadMinutes = 60;

    private const string SalonTimeZone = "Asia/Yekaterinburg";

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Tick_WithBookingInsideWindow_CreatesTheSnapshot()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(59);
        var booking = CreateBooking(startAt);
        context.Source.AddBooking(booking);

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.Reminder, notification.Kind);
        Assert.Equal(UserId, notification.UserId);
        Assert.Equal(booking.BookingId, notification.BookingId);
        Assert.Equal(startAt, notification.StartAt);
        Assert.Equal("Барбершоп", notification.SalonName);
        Assert.Equal("Иван", notification.BarberName);
        Assert.Equal("Стрижка", notification.ServiceName);
        Assert.Equal(SalonTimeZone, notification.SalonTimeZoneId);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
    }

    [Fact]
    public async Task Tick_WithBookingExactlyAtTheLeadBoundary_Creates()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(LeadMinutes)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
    }

    [Fact]
    public async Task Tick_WithBookingOneSecondBeyondTheWindow_DoesNotCreate()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(LeadMinutes).AddSeconds(1)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithBookingStartingNow_DoesNotCreate()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
    }

    [Fact]
    public async Task Tick_WithCancelledBooking_DoesNotCreate()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(30)), isConfirmed: false);

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithGuestBooking_DoesNotCreate()
    {
        var context = CreateContext();
        context.Source.AddGuestBooking(
            Guid.CreateVersion7(),
            Now.AddMinutes(30),
            Now.AddHours(-2));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithBookingCreatedInsideTheWindow_DoesNotCreate()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(
            Now.AddMinutes(59),
            createdAt: Now.AddSeconds(-30)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_Repeated_CreatesOnlyOneRow()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(59)));

        var first = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);
        var second = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(1, first.Value.Created);
        Assert.Equal(0, second.Value.Created);
        Assert.Single(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_UsesTheConfiguredLeadMinutes()
    {
        var context = CreateContext(leadMinutes: 90);
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(89), leadMinutes: 90));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(90, context.Source.LastLeadMinutes);
    }

    [Fact]
    public async Task Tick_WithBookingInsideWindowButTooLate_SkipsAndCounts()
    {
        // 55 минут до визита при окне 60: разница 5 минут больше двух тиков (120 с).
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(55)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Equal(1, result.Value.SkippedLate);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Tick_WithBookingExactlyAtLateToleranceBoundary_Creates()
    {
        // 60 минут окна минус допуск 2 * 60 секунд: ровно на границе ещё создаём.
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(60).AddSeconds(-120)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(0, result.Value.SkippedLate);
    }

    [Fact]
    public async Task Tick_OneSecondBeyondLateTolerance_Skips()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(60).AddSeconds(-121)));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Created);
        Assert.Equal(1, result.Value.SkippedLate);
    }

    [Fact]
    public async Task Tick_WithRepositoryFailure_ReturnsFailureAndStops()
    {
        var context = CreateContext();
        context.Source.AddBooking(CreateBooking(Now.AddMinutes(59)));
        context.Notifications.OnAdd = _ => new Failure(
            Error.Failure("internal.database", "The push notification could not be saved."));

        var result = await context.Handler.Handle(
            new CreateDuePushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", result.Error.Errors[0].Code);
    }

    private static PushReminderBooking CreateBooking(
        DateTimeOffset startAt,
        int leadMinutes = LeadMinutes,
        DateTimeOffset? createdAt = null)
        => new(
            Guid.CreateVersion7(),
            UserId,
            startAt,
            "Стрижка",
            "Барбершоп",
            "Иван",
            SalonTimeZone,
            createdAt ?? startAt.AddMinutes(-leadMinutes));

    private static Context CreateContext(int leadMinutes = LeadMinutes)
    {
        var notifications = new FakePushNotificationRepository();
        var source = new FakePushSourceRepository();
        var clock = new FakeTimeProvider(Now);

        var handler = new CreateDuePushNotificationsHandler(
            notifications,
            source,
            new PushOptions { ReminderLeadMinutes = leadMinutes },
            clock,
            NullLogger<CreateDuePushNotificationsHandler>.Instance);

        return new Context(handler, notifications, source);
    }

    private sealed record Context(
        CreateDuePushNotificationsHandler Handler,
        FakePushNotificationRepository Notifications,
        FakePushSourceRepository Source);
}
