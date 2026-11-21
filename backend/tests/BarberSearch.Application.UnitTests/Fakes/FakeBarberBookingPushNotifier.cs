using BarberSearch.Application.Push.BarberBooking;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberBookingPushNotifier"/> для тестов обработчиков создания
/// и отмены брони.
/// </summary>
/// <remarks>
/// Позволяет проверить, что обработчик передаёт правильный признак «действие выполнил сам
/// мастер», и что сбой уведомления не откатывает бронь: <see cref="Failure"/> подменяет
/// результат постановки в очередь.
/// </remarks>
public sealed class FakeBarberBookingPushNotifier : IBarberBookingPushNotifier
{
    public int Calls { get; private set; }

    public Booking? LastBooking { get; private set; }

    public bool? LastCreatedByBarber { get; private set; }

    public bool? LastCancelledByBarber { get; private set; }

    /// <summary>Результат, который вернёт уведомитель; по умолчанию успех.</summary>
    public Failure? Failure { get; set; }

    public Task<UnitResult<Failure>> NotifyCreatedAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastBooking = booking;
        LastCreatedByBarber = createdByBarber;

        return Task.FromResult(Failure is null
            ? UnitResult.Success<Failure>()
            : UnitResult.Failure(Failure));
    }

    public Task<UnitResult<Failure>> NotifyCancelledAsync(
        Booking booking,
        bool cancelledByBarber,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastBooking = booking;
        LastCancelledByBarber = cancelledByBarber;

        return Task.FromResult(Failure is null
            ? UnitResult.Success<Failure>()
            : UnitResult.Failure(Failure));
    }
}
