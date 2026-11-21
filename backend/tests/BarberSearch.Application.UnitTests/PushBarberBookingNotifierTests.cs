using BarberSearch.Application.Push;
using BarberSearch.Application.Push.BarberBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Постановка в очередь событий мастера о новой записи и её отмене: гейты канала и автора
/// действия, получатель из владельца профиля, снимок имени клиента и дедупликация.
/// </summary>
/// <remarks>
/// Уведомитель не собирает текст и не ходит в сеть: тест проверяет строку очереди (вид,
/// получателя и снимок), а точные тексты живут в <c>PushMessageTests</c>. Получатель —
/// владелец профиля, а не <see cref="Booking.UserId"/>: гостевая бронь клиента-пользователя
/// не имеет, но мастер всё равно должен узнать об отмене.
/// </remarks>
public class PushBarberBookingNotifierTests
{
    private static readonly DateTimeOffset StartAt = new(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid OwnerId = Guid.CreateVersion7();

    [Fact]
    public async Task NotifyCreated_WhenDisabled_DoesNothing()
    {
        var context = CreateContext(enabled: false);

        var result = await context.Notifier.NotifyCreatedAsync(
            context.CreateCustomerBooking(),
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task NotifyCreated_WhenCreatedByBarber_DoesNothing()
    {
        var context = CreateContext();

        var result = await context.Notifier.NotifyCreatedAsync(
            context.CreateCustomerBooking(),
            createdByBarber: true,
            CancellationToken.None);

        // Мастер внёс запись сам: он и есть автор события, push ему не нужен.
        Assert.True(result.IsSuccess);
        Assert.Empty(context.Notifications.Items);
        Assert.DoesNotContain("owner", context.Profiles.LoadCalls);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
    }

    [Fact]
    public async Task NotifyCreated_WhenCreatedByCustomer_EnqueuesTheBarberCreatedKind()
    {
        var context = CreateContext();
        var booking = context.CreateCustomerBooking();

        var result = await context.Notifier.NotifyCreatedAsync(
            booking,
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BarberBookingCreated, notification.Kind);
        Assert.Equal(OwnerId, notification.UserId);
        Assert.Equal(booking.Id, notification.BookingId);
        Assert.Equal(StartAt, notification.StartAt);
        Assert.Equal("Барбершоп", notification.SalonName);
        Assert.Equal("Иван", notification.BarberName);
        Assert.Equal("Стрижка", notification.ServiceName);
        Assert.Equal("Asia/Yekaterinburg", notification.SalonTimeZoneId);
        Assert.Equal("Анна Петрова", notification.CustomerName);
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
    public async Task NotifyCreated_WhenUserLookupFails_StillEnqueuesWithNoName()
    {
        var context = CreateContext();
        context.Users.GetByIdBehavior = _ => new Failure(
            Error.Failure("internal.database", "The user could not be read."));

        var result = await context.Notifier.NotifyCreatedAsync(
            context.CreateCustomerBooking(),
            createdByBarber: false,
            CancellationToken.None);

        // Потерянный пользователь не должен терять уведомление: заголовок деградирует.
        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BarberBookingCreated, notification.Kind);
        Assert.Null(notification.CustomerName);
    }

    [Fact]
    public async Task NotifyCreated_WhenOwnerLookupFails_ReturnsFailureAndWritesNothing()
    {
        var context = CreateContext();
        var booking = context.CreateCustomerBooking(barberProfileId: Guid.CreateVersion7());

        var result = await context.Notifier.NotifyCreatedAsync(
            booking,
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Notifications.AddCalls);
        Assert.Empty(context.Notifications.Items);
    }

    [Fact]
    public async Task NotifyCreated_WhenBookingBelongsToTheOwner_DoesNothing()
    {
        var context = CreateContext();

        var result = await context.Notifier.NotifyCreatedAsync(
            context.CreateBookingForOwner(),
            createdByBarber: false,
            CancellationToken.None);

        // Барбер записал сам себя: событие «клиент записался» было бы ложью.
        Assert.True(result.IsSuccess);
        Assert.Empty(context.Notifications.Items);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
    }

    [Fact]
    public async Task NotifyCreated_Twice_EnqueuesOneRowForTheSameKind()
    {
        var context = CreateContext();
        var booking = context.CreateCustomerBooking();

        var first = await context.Notifier.NotifyCreatedAsync(
            booking,
            createdByBarber: false,
            CancellationToken.None);
        var second = await context.Notifier.NotifyCreatedAsync(
            booking,
            createdByBarber: false,
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(context.Notifications.Items);
        Assert.Equal(2, context.Notifications.AddCalls);
    }

    [Fact]
    public async Task NotifyCancelled_WhenCancelledByBarber_DoesNothing()
    {
        var context = CreateContext();
        var booking = context.CreateCustomerBooking();
        Assert.True(booking.Cancel("Барбер заболел").IsSuccess);

        var result = await context.Notifier.NotifyCancelledAsync(
            booking,
            cancelledByBarber: true,
            CancellationToken.None);

        // Мастер отменил запись сам: второй раз сообщать ему нечего.
        Assert.True(result.IsSuccess);
        Assert.Empty(context.Notifications.Items);
        Assert.DoesNotContain("owner", context.Profiles.LoadCalls);
        Assert.Equal(0, context.Source.GetSnapshotCalls);
    }

    [Fact]
    public async Task NotifyCancelled_WhenCancelledByCustomer_EnqueuesTheBarberCancelledKind()
    {
        var context = CreateContext();
        var booking = context.CreateCustomerBooking();
        Assert.True(booking.Cancel().IsSuccess);

        var result = await context.Notifier.NotifyCancelledAsync(
            booking,
            cancelledByBarber: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BarberBookingCancelled, notification.Kind);
        Assert.Equal(OwnerId, notification.UserId);
        Assert.Equal(booking.Id, notification.BookingId);
        Assert.Equal(StartAt, notification.StartAt);
        Assert.Equal("Стрижка", notification.ServiceName);
        Assert.Equal("Анна Петрова", notification.CustomerName);
    }

    [Fact]
    public async Task NotifyCancelled_WhenGuestCancelledByAdmin_EnqueuesTheGuestName()
    {
        var context = CreateContext();
        var booking = context.CreateGuestBooking();
        Assert.True(booking.Cancel().IsSuccess);

        var result = await context.Notifier.NotifyCancelledAsync(
            booking,
            cancelledByBarber: false,
            CancellationToken.None);

        // У гостя нет аккаунта, но отменить запись может администратор: имя берётся из брони.
        Assert.True(result.IsSuccess);

        var notification = Assert.Single(context.Notifications.Items);
        Assert.Equal(PushNotificationKind.BarberBookingCancelled, notification.Kind);
        Assert.Equal(OwnerId, notification.UserId);
        Assert.Equal("Гость Клиентов", notification.CustomerName);
        Assert.Equal(0, context.Users.GetByIdCallCount);
    }

    private static Context CreateContext(bool enabled = true)
    {
        var source = new FakePushSourceRepository();
        var notifications = new FakePushNotificationRepository();
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();

        var profile = BarberProfile.Create(
            OwnerId,
            "Иван",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;
        profiles.Add(profile);

        var customer = User.Create(
            $"push-barber-customer-{Guid.NewGuid():N}@barbersearch.test",
            "hash",
            "Анна",
            "Петрова").Value;
        users.Add(customer);

        var notifier = new BarberBookingPushNotifier(
            source,
            notifications,
            new PushOptions { Enabled = enabled },
            profiles,
            users);

        return new Context(notifier, source, notifications, profiles, users, profile, customer);
    }

    private sealed record Context(
        BarberBookingPushNotifier Notifier,
        FakePushSourceRepository Source,
        FakePushNotificationRepository Notifications,
        FakeBarberProfileRepository Profiles,
        FakeUserRepository Users,
        BarberProfile Profile,
        User Customer)
    {
        public Booking CreateCustomerBooking(Guid? barberProfileId = null)
            => CreateBooking(Customer.Id, barberProfileId ?? Profile.Id);

        public Booking CreateBookingForOwner()
            => CreateBooking(OwnerId, Profile.Id);

        public Booking CreateGuestBooking()
            => Booking.Create(
                userId: null,
                Profile.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                StartAt,
                StartAt.AddMinutes(30),
                1500m,
                $"key-{Guid.NewGuid()}",
                guestFirstName: "Гость",
                guestLastName: "Клиентов",
                guestPhone: "+79990000000").Value;

        private static Booking CreateBooking(Guid userId, Guid barberProfileId)
            => Booking.Create(
                userId,
                barberProfileId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                StartAt,
                StartAt.AddMinutes(30),
                1500m,
                $"key-{Guid.NewGuid()}").Value;
    }
}
