using BarberSearch.Application.Push;
using BarberSearch.Application.Push.BookingCreated;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Постановка в очередь события создания брони: гейты канала и гостя, вид уведомления,
/// дедупликация и деградация без снимка.
/// </summary>
/// <remarks>
/// Уведомитель не собирает текст и не ходит в сеть: тест проверяет строку очереди (вид и
/// снимок), а точные тексты живут в <c>PushMessageTests</c>. Бронь создаётся доменной
/// фабрикой, статус для постановки в очередь значения не имеет.
/// </remarks>
public class PushBookingCreatedNotifierTests
{
    private static readonly DateTimeOffset StartAt = new(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Notify_WhenDisabled_DoesNothing()
    {
        var context = CreateContext(enabled: false);

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            createdByBarber: false,
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
            createdByBarber: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_WhenCreatedByCustomer_EnqueuesTheCustomerKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();

        var result = await context.Notifier.NotifyAsync(
            booking,
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, notification.Kind);
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
    public async Task Notify_WhenCreatedByBarber_EnqueuesTheBarberKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();

        var result = await context.Notifier.NotifyAsync(
            booking,
            createdByBarber: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingCreatedByBarber, notification.Kind);
    }

    [Fact]
    public async Task Notify_CustomerCreatedFromApp_DoesNotEnqueue()
    {
        var context = CreateContext(isMobileApp: true);

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            createdByBarber: false,
            CancellationToken.None);

        // Экран приложения уже подтвердил запись: push о своём действии не нужен.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task Notify_CustomerCreatedFromWeb_EnqueuesCreatedByCustomer()
    {
        var context = CreateContext(isMobileApp: false);

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, notification.Kind);
    }

    [Fact]
    public async Task Notify_BarberCreatedFromApp_StillEnqueues()
    {
        // Заголовок приложения не гейтит ручную запись барбера: клиент о ней не знал.
        var context = CreateContext(isMobileApp: true);

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            createdByBarber: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BookingCreatedByBarber, notification.Kind);
    }

    [Fact]
    public async Task Notify_Twice_EnqueuesOneRowForTheSameKind()
    {
        var context = CreateContext();
        var booking = CreateBooking();

        var first = await context.Notifier.NotifyAsync(booking, false, CancellationToken.None);
        var second = await context.Notifier.NotifyAsync(booking, false, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(context.Notifications.Items);
        Assert.Equal(2, context.Notifications.AddCalls);
    }

    [Fact]
    public async Task Notify_WhenReminderAlreadyExists_StillEnqueuesTheCreationKind()
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

        var result = await context.Notifier.NotifyAsync(booking, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Notifications.Items.Count);
        Assert.Contains(
            context.Notifications.Items,
            notification => notification.Kind == PushNotificationKind.Reminder);
        Assert.Contains(
            context.Notifications.Items,
            notification => notification.Kind == PushNotificationKind.BookingCreatedByCustomer);
    }

    [Fact]
    public async Task Notify_WhenSnapshotIsMissing_EnqueuesTheDegradedRow()
    {
        var context = CreateContext();
        context.Source.Snapshot = null;

        var result = await context.Notifier.NotifyAsync(
            CreateBooking(),
            createdByBarber: false,
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
            createdByBarber: false,
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
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", result.Error.Errors[0].Code);
        Assert.Empty(context.Notifications.Items);
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

    private static Context CreateContext(bool enabled = true, bool isMobileApp = false)
    {
        var source = new FakePushSourceRepository();
        var notifications = new FakePushNotificationRepository();
        var clientSurface = new FakeClientSurface { IsMobileApp = isMobileApp };
        var notifier = new BookingCreatedPushNotifier(
            source,
            notifications,
            new PushOptions { Enabled = enabled },
            clientSurface);

        return new Context(notifier, source, notifications, clientSurface);
    }

    private sealed record Context(
        BookingCreatedPushNotifier Notifier,
        FakePushSourceRepository Source,
        FakePushNotificationRepository Notifications,
        FakeClientSurface ClientSurface);
}
