using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки разбора data-payload'а напоминания: тип сообщения и цель перехода.
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
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = "booking.cancelled",
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
