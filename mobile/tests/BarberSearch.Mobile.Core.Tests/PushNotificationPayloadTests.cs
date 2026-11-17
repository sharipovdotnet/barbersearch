using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки разбора data-payload'а push-сообщений: тип сообщения и цель перехода.
/// </summary>
/// <remarks>
/// Разбор чистый (без MAUI и Shell), поэтому проверяется обычным словарём: ровно те extras,
/// которые кладёт сервер и которые читает <c>MainActivity</c>.
/// </remarks>
public sealed class PushNotificationPayloadTests
{
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");

    [Fact]
    public void TryParse_ReminderExtras_OpensBookingsTab()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.ReminderType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.ReminderType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_ReminderWithoutBookingId_StillOpensBookingsTab()
    {
        // Deep link на запись не делается: GET api/bookings/{id} не существует, а список
        // «Записи» и так содержит нужную строку.
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.ReminderType,
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.Null(payload!.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_BookingCreatedExtras_OpensBookingsTab()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingCreatedType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingCreatedType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_BookingCreatedWithoutBookingId_StillOpensBookingsTab()
    {
        // Событие создания приходит и без bookingId: и «вы записались», и «вас записали»
        // открывают список «Записи», а не карточку записи.
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingCreatedType,
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.Null(payload!.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_BookingConfirmedExtras_OpensBookingsTab()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingConfirmedType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingConfirmedType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_BookingCancelledExtras_OpensBookingsTab()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingCancelledType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingCancelledType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_BookingRescheduledExtras_OpensBookingsTab()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingRescheduledType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingRescheduledType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void Payload_CustomerCancelled_RoutesToBookings()
    {
        // Отмена клиентом с веба: приложение получает push, потому что само действие
        // происходило не в нём.
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingCancelledByCustomerType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingCancelledByCustomerType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void Payload_CustomerRescheduled_RoutesToBookings()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.BookingRescheduledByCustomerType,
            ["bookingId"] = BookingId.ToString("D"),
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(PushNotificationPayload.BookingRescheduledByCustomerType, payload.Type);
        Assert.Equal(BookingId, payload.BookingId);
        Assert.Equal("//bookings", payload.Route);
    }

    [Fact]
    public void TryParse_InvalidBookingId_LeavesBookingIdEmpty()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = PushNotificationPayload.ReminderType,
            ["bookingId"] = "not-a-guid",
        };

        var parsed = PushNotificationPayload.TryParse(extras, out var payload);

        Assert.True(parsed);
        Assert.Null(payload!.BookingId);
    }

    [Fact]
    public void TryParse_UnknownType_HasNoTarget()
    {
        // Тип из будущего среза: приложение не должно открывать вкладку, которую это
        // уведомление не имело в виду.
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "booking.something",
            ["bookingId"] = BookingId.ToString("D"),
        };

        Assert.False(PushNotificationPayload.TryParse(extras, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_MissingType_HasNoTarget()
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bookingId"] = BookingId.ToString("D"),
        };

        Assert.False(PushNotificationPayload.TryParse(extras, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_NullExtras_HasNoTarget()
    {
        Assert.False(PushNotificationPayload.TryParse(null, out var payload));
        Assert.Null(payload);
    }
}
