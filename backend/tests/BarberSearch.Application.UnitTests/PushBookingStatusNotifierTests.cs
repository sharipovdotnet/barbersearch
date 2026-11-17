using BarberSearch.Application.Push;
using BarberSearch.Application.Push.BookingStatus;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Постановка в очередь событий статуса брони: гейты канала и гостя, отображение смены
/// статуса в вид уведомления, новый <c>start_at</c> после переноса, дедупликация и
/// деградация без снимка.
/// </summary>
/// <remarks>
/// Уведомитель не собирает текст и не ходит в сеть: тест проверяет строку очереди (вид и
/// снимок), а точные тексты живут в <c>PushMessageTests</c>. Бронь создаётся доменной
/// фабрикой; статус для постановки в очередь значения не имеет — его уже сохранил обработчик.
/// </remarks>
public class PushBookingStatusNotifierTests
{
    private static readonly DateTimeOffset StartAt = new(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Notify_WhenDisabled_DoesNothing()
    {
        var context = CreateContext(enabled: false);

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenBookingIsGuest_DoesNothing()
    {
        var context = CreateContext();

        var result = await context.Notifier.NotifyAsync(
            CreateGuestBooking(),
            BookingStatusChange.CancelledByBarber,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenConfirmed_EnqueuesTheConfirmedKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();

        var result = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingConfirmed, notification.Kind);
        Assert.Equal(UserId, notification.UserId);
        Assert.Equal(booking.Id, notification.BookingId);
        Assert.Equal(StartAt, notification.StartAt);
        Assert.Equal("Барбершоп", notification.SalonName);
        Assert.Equal("Иван", notification.BarberName);
        Assert.Equal("Стрижка", notification.ServiceName);
        Assert.Equal("Asia/Yekaterinburg", notification.SalonTimeZoneId);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.NotNull(context.Source.LastSnapshotKey);
        Assert.Equal(
            booking.BarberProfileId,
            context.Source.LastSnapshotKey.Value.BarberProfileId);
        Assert.Equal(
            booking.BarberServiceId,
            context.Source.LastSnapshotKey.Value.BarberServiceId);
        Assert.Equal(booking.SalonId, context.Source.LastSnapshotKey.Value.SalonId);
    }

    [Fact]
    public async Task Notify_WhenCancelledByBarber_EnqueuesTheCancelledKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();
        Assert.True(booking.Cancel("Барбер заболел").IsSuccess);

        var result = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.CancelledByBarber,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PushNotificationKind.BookingCancelledByBarber,
            Assert.Single(context.Notifications.Items).Kind);

        // Причина отмены остаётся в брони и в строку push не попадает: у таблицы нет
        // колонки под свободный текст, поэтому и в сообщении её взяться неоткуда.
    }

    [Fact]
    public async Task Notify_WhenRescheduledByBarber_EnqueuesTheNewStartTime()
    {
        var context = CreateContext();
        var booking = CreateBooking();
        var newStart = StartAt.AddHours(3);
        Assert.True(booking.Reschedule(newStart, newStart.AddMinutes(30)).IsSuccess);

        var result = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.RescheduledByBarber,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingRescheduledByBarber, notification.Kind);
        Assert.Equal(newStart, notification.StartAt);
    }

    [Fact]
    public async Task Notify_TwiceForTheSameStart_EnqueuesOneRow()
    {
        var context = CreateContext();
        var booking = CreateBooking();

        var first = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.Confirmed,
            CancellationToken.None);
        var second = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(context.Notifications.Items);
        Assert.Equal(2, context.Notifications.AddCalls);
    }

    [Fact]
    public async Task Notify_TwiceForDifferentStarts_EnqueuesTwoRescheduleRows()
    {
        var context = CreateContext();
        var booking = CreateBooking();
        var firstStart = StartAt.AddHours(1);
        Assert.True(booking.Reschedule(firstStart, firstStart.AddMinutes(30)).IsSuccess);

        var first = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.RescheduledByBarber,
            CancellationToken.None);

        var secondStart = StartAt.AddHours(2);
        Assert.True(booking.Reschedule(secondStart, secondStart.AddMinutes(30)).IsSuccess);

        var second = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.RescheduledByBarber,
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        // start_at входит в уникальный индекс: каждый новый целевой момент — новое событие,
        // а не дубль предыдущего переноса.
        Assert.Equal(2, context.Notifications.Items.Count);
        Assert.Contains(context.Notifications.Items, row => row.StartAt == firstStart);
        Assert.Contains(context.Notifications.Items, row => row.StartAt == secondStart);
    }

    [Fact]
    public async Task Notify_WhenSnapshotIsMissing_EnqueuesTheDegradedRow()
    {
        var context = CreateContext();
        context.Source.Snapshot = null;

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            BookingStatusChange.RescheduledByBarber,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Null(notification.SalonName);
        Assert.Null(notification.BarberName);
        Assert.Null(notification.ServiceName);
        Assert.Null(notification.SalonTimeZoneId);
    }

    [Fact]
    public async Task Notify_WhenSnapshotFails_ReturnsFailureAndWritesNothing()
    {
        var context = CreateContext();
        context.Source.SnapshotFailure = new Failure(
            Error.Failure("internal.database", "The snapshot could not be read."));

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenRepositoryFails_ReturnsFailure()
    {
        var context = CreateContext();
        context.Notifications.OnAdd = _ => new Failure(
            Error.Failure("internal.database", "The push notification could not be saved."));

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", result.Error.Errors[0].Code);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenTheSameKindAlreadyExists_ReturnsSuccessWithoutASecondRow()
    {
        var context = CreateContext();
        var booking = CreateBooking();
        context.Notifications.Seed(PushNotification.Create(
            PushNotificationKind.BookingConfirmed,
            UserId,
            booking.Id,
            StartAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Asia/Yekaterinburg").Value);

        var result = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenReminderAlreadyExists_StillEnqueuesTheStatusKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();
        context.Notifications.Seed(PushNotification.Create(
            PushNotificationKind.Reminder,
            UserId,
            booking.Id,
            StartAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Asia/Yekaterinburg").Value);

        var result = await context.Notifier.NotifyAsync(
            booking,
            BookingStatusChange.Confirmed,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Notifications.Items.Count);
        Assert.Contains(
            context.Notifications.Items,
            notification => notification.Kind == PushNotificationKind.Reminder);
        Assert.Contains(
            context.Notifications.Items,
            notification => notification.Kind == PushNotificationKind.BookingConfirmed);
    }

    private static Booking CreateBooking()
        => Booking.Create(
            UserId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            StartAt,
            StartAt.AddMinutes(30),
            1500m,
            $"key-{Guid.NewGuid()}").Value;

    private static Booking CreateGuestBooking()
        => Booking.Create(
            userId: null,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            StartAt,
            StartAt.AddMinutes(30),
            1500m,
            $"key-{Guid.NewGuid()}",
            guestFirstName: "Гость",
            guestPhone: "+70000000000").Value;

    private static Context CreateContext(bool enabled = true)
    {
        var source = new FakePushSourceRepository();
        var notifications = new FakePushNotificationRepository();
        var notifier = new BookingStatusPushNotifier(
            source,
            notifications,
            new PushOptions { Enabled = enabled });

        return new Context(notifier, source, notifications);
    }

    private sealed record Context(
        BookingStatusPushNotifier Notifier,
        FakePushSourceRepository Source,
        FakePushNotificationRepository Notifications);
}
