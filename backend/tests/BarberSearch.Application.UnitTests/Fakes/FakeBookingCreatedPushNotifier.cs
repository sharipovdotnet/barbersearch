using BarberSearch.Application.Push.BookingCreated;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBookingCreatedPushNotifier"/> для тестов <c>CreateBookingHandler</c>.
/// </summary>
/// <remarks>
/// Позволяет проверить, что обработчик передаёт правильный признак «создал барбер», и что
/// сбой уведомления не откатывает бронь: <see cref="Failure"/> и <see cref="Exception"/>
/// подменяют результат постановки в очередь.
/// </remarks>
public sealed class FakeBookingCreatedPushNotifier : IBookingCreatedPushNotifier
{
    public int Calls { get; private set; }

    public Booking? LastBooking { get; private set; }

    public bool? LastCreatedByBarber { get; private set; }

    /// <summary>Результат, который вернёт уведомитель; по умолчанию успех.</summary>
    public Failure? Failure { get; set; }

    /// <summary>Исключение, которое уведомитель бросит вместо возврата результата.</summary>
    public Exception? Exception { get; set; }

    public Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastBooking = booking;
        LastCreatedByBarber = createdByBarber;

        if (Exception is not null)
        {
            throw Exception;
        }

        return Task.FromResult(Failure is null
            ? UnitResult.Success<Failure>()
            : UnitResult.Failure(Failure));
    }
}
