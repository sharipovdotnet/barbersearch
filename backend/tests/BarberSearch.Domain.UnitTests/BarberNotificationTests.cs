using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Инварианты уведомления барберу (§2 спецификации варианта C): соответствие полей виду,
/// допустимые переходы доставки, идемпотентная отметка прочтения и предел длины ошибки.
/// </summary>
public class BarberNotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly LocalDate = new(2026, 11, 10);

    private static readonly DateTimeOffset ReadAt = new(2026, 11, 10, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Upcoming_WithValidData_StartsPending()
    {
        var result = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            "стрижка",
            "Барбершоп на Ленина",
            "Asia/Yekaterinburg");

        Assert.True(result.IsSuccess);

        var notification = result.Value;

        Assert.Equal(BarberNotificationKind.Upcoming, notification.Kind);
        Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);
        Assert.Equal(0, notification.DeliveryAttempts);
        Assert.Null(notification.ReadAt);
        Assert.Null(notification.BookingsCount);
        Assert.Null(notification.DigestLocalDate);
        Assert.Equal("стрижка", notification.ServiceName);
        Assert.Equal("Барбершоп на Ленина", notification.SalonName);
        Assert.Equal("Asia/Yekaterinburg", notification.SalonTimeZoneId);
    }

    [Fact]
    public void Upcoming_WithoutBooking_ReturnsInvalid()
    {
        var result = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.Empty,
            Start,
            "стрижка",
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.create.invalid", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public void Upcoming_WithoutStartAt_ReturnsInvalid()
    {
        var result = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            default,
            "стрижка",
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Errors, error => error.Code == "notification.create.invalid");
    }

    [Fact]
    public void Upcoming_WithoutRecipient_ReturnsInvalid()
    {
        var result = BarberNotification.Upcoming(
            Guid.Empty,
            Guid.CreateVersion7(),
            Start,
            "стрижка",
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Errors, error => error.Code == "notification.create.invalid");
    }

    [Fact]
    public void Upcoming_WithTooLongServiceName_ReturnsServiceNameCode()
    {
        var tooLong = new string('a', Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH + 1);

        var result = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            tooLong,
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.create.serviceName", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Upcoming_WithTooLongTimeZone_ReturnsTimeZoneCode()
    {
        var tooLong = new string('a', Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH + 1);

        var result = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            "стрижка",
            "Салон",
            tooLong);

        Assert.True(result.IsFailure);
        Assert.Equal("notification.create.salonTimeZoneId", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Upcoming_TrimsSnapshotsAndStoresBlankAsNull()
    {
        var notification = BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            "  стрижка  ",
            "   ",
            null).Value;

        Assert.Equal("стрижка", notification.ServiceName);
        Assert.Null(notification.SalonName);
        Assert.Null(notification.SalonTimeZoneId);
    }

    [Fact]
    public void Digest_WithValidData_KeepsCountAndDate()
    {
        var result = BarberNotification.Digest(
            Guid.CreateVersion7(),
            4,
            LocalDate,
            Start,
            "стрижка",
            "Салон",
            "Asia/Yekaterinburg");

        Assert.True(result.IsSuccess);

        var notification = result.Value;

        Assert.Equal(BarberNotificationKind.Digest, notification.Kind);
        Assert.Equal(4, notification.BookingsCount);
        Assert.Equal(LocalDate, notification.DigestLocalDate);
        Assert.Equal(Start, notification.StartAt);
        Assert.Null(notification.BookingId);
    }

    [Fact]
    public void Digest_WithoutLocalDate_ReturnsInvalid()
    {
        var result = BarberNotification.Digest(
            Guid.CreateVersion7(),
            4,
            default,
            Start,
            "стрижка",
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.create.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Digest_WithNegativeCount_ReturnsInvalid()
    {
        var result = BarberNotification.Digest(
            Guid.CreateVersion7(),
            -1,
            LocalDate,
            Start,
            "стрижка",
            "Салон",
            "UTC");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.create.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Digest_WithZeroBookings_IsAllowedByTheInvariant()
    {
        // Ноль допустим как значение; такой дайджест не создаёт Application — сообщать нечего.
        var result = BarberNotification.Digest(
            Guid.CreateVersion7(),
            0,
            LocalDate,
            null,
            null,
            null,
            "UTC");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.BookingsCount);
    }

    [Fact]
    public void MarkSent_FromPending_MarksSent()
    {
        var notification = CreateUpcoming();

        var result = notification.MarkSent();

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberNotificationDelivery.Sent, notification.Delivery);
        Assert.Null(notification.NextAttemptAt);
    }

    [Fact]
    public void MarkSent_Twice_ReturnsInvalidStatus()
    {
        var notification = CreateUpcoming();
        Assert.True(notification.MarkSent().IsSuccess);

        var repeated = notification.MarkSent();

        Assert.True(repeated.IsFailure);
        Assert.Equal("notification.markSent.invalidStatus", repeated.Error.Errors[0].Code);
        Assert.Equal(ErrorType.CONFLICT, repeated.Error.Errors[0].Type);
    }

    [Fact]
    public void MarkSkipped_AfterSent_IsRejected()
    {
        var notification = CreateUpcoming();
        Assert.True(notification.MarkSent().IsSuccess);

        var result = notification.MarkSkipped("booking is not confirmed");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.markSkipped.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(BarberNotificationDelivery.Sent, notification.Delivery);
    }

    [Fact]
    public void MarkFailed_AfterSkipped_IsRejected()
    {
        var notification = CreateUpcoming();
        Assert.True(notification.MarkSkipped("telegram is not linked").IsSuccess);

        var result = notification.MarkFailed("timeout");

        Assert.True(result.IsFailure);
        Assert.Equal("notification.markFailed.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(BarberNotificationDelivery.Skipped, notification.Delivery);
        Assert.Equal(0, notification.DeliveryAttempts);
    }

    [Fact]
    public void MarkFailed_BeforeTheLastAttempt_KeepsPendingAndSchedulesRetry()
    {
        var notification = CreateUpcoming();
        var nextAttemptAt = Start.AddMinutes(1);

        var result = notification.MarkFailed("timeout", nextAttemptAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);
        Assert.Equal(1, notification.DeliveryAttempts);
        Assert.Equal(nextAttemptAt, notification.NextAttemptAt);
        Assert.Equal("timeout", notification.LastError);
    }

    [Fact]
    public void MarkFailed_OnTheFifthAttempt_BecomesFailedForever()
    {
        var notification = CreateUpcoming();

        for (var attempt = 1; attempt < Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS; attempt++)
        {
            Assert.True(notification.MarkFailed($"error {attempt}", Start).IsSuccess);
            Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);
        }

        var last = notification.MarkFailed("final error", Start.AddMinutes(15));

        Assert.True(last.IsSuccess);
        Assert.Equal(BarberNotificationDelivery.Failed, notification.Delivery);
        Assert.Equal(Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS, notification.DeliveryAttempts);
        Assert.Null(notification.NextAttemptAt);

        var extra = notification.MarkFailed("one more", Start.AddMinutes(30));

        Assert.True(extra.IsFailure);
        Assert.Equal("notification.markFailed.invalidStatus", extra.Error.Errors[0].Code);
        Assert.Equal(Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS, notification.DeliveryAttempts);
    }

    [Fact]
    public void MarkFailed_TruncatesLastErrorToTheColumnLimit()
    {
        var notification = CreateUpcoming();
        var tooLong = new string('e', Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH + 100);

        Assert.True(notification.MarkFailed(tooLong).IsSuccess);

        Assert.NotNull(notification.LastError);
        Assert.Equal(Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH, notification.LastError!.Length);
    }

    [Fact]
    public void MarkRead_SetsReadAt()
    {
        var notification = CreateUpcoming();

        var result = notification.MarkRead(ReadAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReadAt, notification.ReadAt);
    }

    [Fact]
    public void MarkRead_IsIdempotent()
    {
        var notification = CreateUpcoming();
        Assert.True(notification.MarkRead(ReadAt).IsSuccess);

        var repeated = notification.MarkRead(ReadAt.AddHours(1));

        Assert.True(repeated.IsSuccess);
        Assert.Equal(ReadAt, notification.ReadAt);
    }

    [Fact]
    public void MarkRead_WithoutTime_ReturnsValidationFailure()
    {
        var notification = CreateUpcoming();

        var result = notification.MarkRead(default);

        Assert.True(result.IsFailure);
        Assert.Equal("notification.markRead.readAt", result.Error.Errors[0].Code);
        Assert.Null(notification.ReadAt);
    }

    private static BarberNotification CreateUpcoming()
        => BarberNotification.Upcoming(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            "стрижка",
            "Барбершоп на Ленина",
            "Asia/Yekaterinburg").Value;
}
