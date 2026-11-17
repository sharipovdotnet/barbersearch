using BarberSearch.Domain.Push;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Push-напоминание: фабрика-снимок и переходы доставки.
/// </summary>
/// <remarks>
/// Предел попыток и расписание повторов — политика Application, поэтому момент следующей
/// попытки передаётся в <c>MarkFailed</c>; домен только согласованно применяет её и на
/// пятом провале ставит терминальное <c>Failed</c>.
/// </remarks>
public class PushNotificationTests
{
    private static readonly DateTimeOffset StartAt = new(2026, 11, 15, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    private static readonly Guid BookingId = Guid.CreateVersion7();

    [Fact]
    public void Create_WithoutRecipientAndBooking_ReturnsAllErrors()
    {
        var result = PushNotification.Create(
            PushNotificationKind.Reminder,
            Guid.Empty,
            Guid.Empty,
            default,
            null,
            null,
            null,
            null);

        Assert.True(result.IsFailure);
        Assert.Equal(3, result.Error.Errors.Count);
        Assert.All(
            result.Error.Errors,
            error => Assert.Equal("pushNotification.create.invalid", error.Code));
    }

    [Fact]
    public void Create_WithTooLongSnapshots_ReturnsEachFieldError()
    {
        var result = PushNotification.Create(
            PushNotificationKind.Reminder,
            UserId,
            BookingId,
            StartAt,
            new string('s', 201),
            new string('b', 121),
            new string('v', 201),
            new string('z', 101));

        Assert.True(result.IsFailure);
        Assert.Equal(4, result.Error.Errors.Count);
        Assert.Contains(result.Error.Errors, error => error.Code == "pushNotification.create.salonName");
        Assert.Contains(result.Error.Errors, error => error.Code == "pushNotification.create.barberName");
        Assert.Contains(result.Error.Errors, error => error.Code == "pushNotification.create.serviceName");
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "pushNotification.create.salonTimeZoneId");
    }

    [Fact]
    public void Create_WithValidSnapshot_StartsPendingAndNormalizesBlanks()
    {
        var result = PushNotification.Create(
            PushNotificationKind.Reminder,
            UserId,
            BookingId,
            StartAt,
            "  Барбершоп  ",
            "   ",
            " Стрижка ",
            "Asia/Yekaterinburg");

        Assert.True(result.IsSuccess);

        var notification = result.Value;

        Assert.Equal(UserId, notification.UserId);
        Assert.Equal(BookingId, notification.BookingId);
        Assert.Equal(StartAt, notification.StartAt);
        Assert.Equal("Барбершоп", notification.SalonName);
        Assert.Null(notification.BarberName);
        Assert.Equal("Стрижка", notification.ServiceName);
        Assert.Equal("Asia/Yekaterinburg", notification.SalonTimeZoneId);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.Equal(0, notification.DeliveryAttempts);
        Assert.Null(notification.NextAttemptAt);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void Create_WithBookingCreatedByCustomerKind_StoresTheKind()
    {
        var result = PushNotification.Create(
            PushNotificationKind.BookingCreatedByCustomer,
            UserId,
            BookingId,
            StartAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Asia/Yekaterinburg");

        Assert.True(result.IsSuccess);
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, result.Value.Kind);
    }

    [Fact]
    public void Create_WithBookingCreatedByBarberKind_StoresTheKind()
    {
        var result = PushNotification.Create(
            PushNotificationKind.BookingCreatedByBarber,
            UserId,
            BookingId,
            StartAt,
            null,
            null,
            null,
            null);

        Assert.True(result.IsSuccess);
        Assert.Equal(PushNotificationKind.BookingCreatedByBarber, result.Value.Kind);
    }

    [Fact]
    public void Create_WithUndefinedKind_ReturnsValidationError()
    {
        var result = PushNotification.Create(
            (PushNotificationKind)99,
            UserId,
            BookingId,
            StartAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Asia/Yekaterinburg");

        Assert.True(result.IsFailure);
        Assert.Equal("pushNotification.create.invalid", result.Error.Errors.Single().Code);
    }

    [Fact]
    public void PushNotificationKind_Values_AreStableForTheDatabaseConstraint()
    {
        // Числа попадают в check ck_push_notifications_kind и в уникальный индекс
        // (booking_id, kind, start_at): менять их без миграции нельзя.
        Assert.Equal(1, (int)PushNotificationKind.Reminder);
        Assert.Equal(2, (int)PushNotificationKind.BookingCreatedByCustomer);
        Assert.Equal(3, (int)PushNotificationKind.BookingCreatedByBarber);
        Assert.Equal(4, (int)PushNotificationKind.BookingConfirmed);
        Assert.Equal(5, (int)PushNotificationKind.BookingCancelledByBarber);
        Assert.Equal(6, (int)PushNotificationKind.BookingRescheduledByBarber);
    }

    [Fact]
    public void MarkSent_FromPending_BecomesSentAndClearsNextAttempt()
    {
        var notification = Create();

        Assert.True(notification.MarkFailed("push.send", StartAt).IsSuccess);
        Assert.NotNull(notification.NextAttemptAt);

        var result = notification.MarkSent();

        Assert.True(result.IsSuccess);
        Assert.Equal(PushDelivery.Sent, notification.Delivery);
        Assert.Null(notification.NextAttemptAt);
    }

    [Fact]
    public void MarkSent_FromTerminalState_ReturnsConflict()
    {
        var notification = Create();
        Assert.True(notification.MarkSkipped("no registered device").IsSuccess);

        var result = notification.MarkSent();

        Assert.True(result.IsFailure);
        Assert.Equal("pushNotification.markSent.invalidStatus", result.Error.Errors[0].Code);
    }

    [Fact]
    public void MarkSkipped_FromPending_StoresTheReasonWithoutPersonalData()
    {
        var notification = Create();

        var result = notification.MarkSkipped("  booking is not confirmed  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(PushDelivery.Skipped, notification.Delivery);
        Assert.Equal("booking is not confirmed", notification.LastError);
        Assert.Null(notification.NextAttemptAt);
    }

    [Fact]
    public void MarkFailed_BeforeTheLimit_StaysPendingAndSchedulesTheNextAttempt()
    {
        var notification = Create();
        var nextAttemptAt = StartAt.AddMinutes(1);

        var result = notification.MarkFailed("push.send", nextAttemptAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.Equal(1, notification.DeliveryAttempts);
        Assert.Equal(nextAttemptAt, notification.NextAttemptAt);
        Assert.Equal("push.send", notification.LastError);
    }

    [Fact]
    public void MarkFailed_OnTheFifthAttempt_BecomesTerminalFailed()
    {
        var notification = Create();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.True(notification.MarkFailed("push.send", StartAt).IsSuccess);
            Assert.Equal(PushDelivery.Pending, notification.Delivery);
        }

        var result = notification.MarkFailed("push.send", StartAt.AddMinutes(15));

        Assert.True(result.IsSuccess);
        Assert.Equal(PushDelivery.Failed, notification.Delivery);
        Assert.Equal(5, notification.DeliveryAttempts);
        Assert.Null(notification.NextAttemptAt);
    }

    [Fact]
    public void MarkFailed_WithASmallerConfiguredBudget_FailsEarlier()
    {
        var notification = Create();

        Assert.True(notification.MarkFailed("push.send", StartAt, maxAttempts: 2).IsSuccess);
        Assert.True(notification.MarkFailed("push.send", StartAt, maxAttempts: 2).IsSuccess);

        Assert.Equal(PushDelivery.Failed, notification.Delivery);
        Assert.Equal(2, notification.DeliveryAttempts);
    }

    [Fact]
    public void MarkFailed_AfterTerminalState_ReturnsConflict()
    {
        var notification = Create();
        Assert.True(notification.MarkSent().IsSuccess);

        var result = notification.MarkFailed("push.send", StartAt);

        Assert.True(result.IsFailure);
        Assert.Equal("pushNotification.markFailed.invalidStatus", result.Error.Errors[0].Code);
    }

    private static PushNotification Create()
        => PushNotification.Create(
            PushNotificationKind.Reminder,
            UserId,
            BookingId,
            StartAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Asia/Yekaterinburg").Value;
}
