using BarberSearch.Application.Push.BookingStatus;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBookingStatusPushNotifier"/> для тестов подтверждения, отмены и
/// переноса брони.
/// </summary>
/// <remarks>
/// Позволяет проверить, что обработчик передаёт правильную смену статуса и что для переноса
/// уведомление видит уже новое время (<see cref="LastStartAt"/> снимается в момент вызова,
/// а не читается из мутируемой позже брони), и что сбой уведомления не откатывает бронь.
/// </remarks>
public sealed class FakeBookingStatusPushNotifier : IBookingStatusPushNotifier
{
    public int Calls { get; private set; }

    public Booking? LastBooking { get; private set; }

    public BookingStatusChange? LastChange { get; private set; }

    /// <summary>Начало визита в момент вызова: для переноса — уже новое время.</summary>
    public DateTimeOffset? LastStartAt { get; private set; }

    /// <summary>Результат, который вернёт уведомитель; по умолчанию успех.</summary>
    public Failure? Failure { get; set; }

    /// <summary>Исключение, которое уведомитель бросит вместо возврата результата.</summary>
    public Exception? Exception { get; set; }

    public Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        BookingStatusChange change,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastBooking = booking;
        LastChange = change;
        LastStartAt = booking.StartAt;

        if (Exception is not null)
        {
            throw Exception;
        }

        return Task.FromResult(Failure is null
            ? UnitResult.Success<Failure>()
            : UnitResult.Failure(Failure));
    }
}
