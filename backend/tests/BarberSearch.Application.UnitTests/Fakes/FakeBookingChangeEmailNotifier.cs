using BarberSearch.Application.Bookings.BookingChangeEmail;
using BarberSearch.Domain.Bookings;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBookingChangeEmailNotifier"/>: запоминает вызовы и возвращает успех.
/// </summary>
/// <remarks>
/// Гейт поверхности и подтверждения адреса живёт в настоящем уведомителе, поэтому обработчики
/// вызывают порт всегда, а этот фейк проверяет лишь то, что они передали правильный признак
/// авторства и уже сохранённое время визита, — без поднятия outbox и репозиториев.
/// </remarks>
public sealed class FakeBookingChangeEmailNotifier : IBookingChangeEmailNotifier
{
    /// <summary>Сколько раз обработчик ставил письмо об отмене.</summary>
    public int CancelledCalls { get; private set; }

    /// <summary>Сколько раз обработчик ставил письмо о переносе.</summary>
    public int RescheduledCalls { get; private set; }

    /// <summary>Бронь последнего вызова: для переноса — уже с новым временем.</summary>
    public Booking? LastBooking { get; private set; }

    /// <summary>Признак «действие клиента-владельца» последнего вызова.</summary>
    public bool? LastIsClientOwner { get; private set; }

    /// <summary>Начало визита в момент вызова: для переноса — уже новое время.</summary>
    public DateTimeOffset? LastStartAt { get; private set; }

    public Task NotifyCancelledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default)
    {
        CancelledCalls++;
        Capture(booking, isClientOwner);

        return Task.CompletedTask;
    }

    public Task NotifyRescheduledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default)
    {
        RescheduledCalls++;
        Capture(booking, isClientOwner);

        return Task.CompletedTask;
    }

    private void Capture(Booking booking, bool isClientOwner)
    {
        LastBooking = booking;
        LastIsClientOwner = isClientOwner;
        LastStartAt = booking.StartAt;
    }
}
