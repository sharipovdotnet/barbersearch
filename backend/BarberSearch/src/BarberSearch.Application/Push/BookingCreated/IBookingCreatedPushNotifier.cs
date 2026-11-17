using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BookingCreated;

/// <summary>
/// Ставит в очередь push-событие о только что созданной брони.
/// </summary>
/// <remarks>
/// <para>
/// Вызывается из <c>CreateBookingHandler</c> внутри транзакции создания брони, поэтому
/// уведомление и бронь коммитятся вместе: либо есть обе строки, либо ни одной. Воркер
/// <c>PushDispatchService</c> доставит событие на следующем тике (≤ <c>Push:IntervalSeconds</c>).
/// </para>
/// <para>
/// Гостю push не ставится: у <c>Booking.UserId</c> = <c>null</c>, отправлять некуда. Ошибка
/// постановки в очередь не должна ронять создание брони — решение принимает вызывающий.
/// </para>
/// </remarks>
public interface IBookingCreatedPushNotifier
{
    /// <summary>
    /// Создаёт строку push-очереди для брони, если канал включён и у брони есть клиент.
    /// </summary>
    /// <param name="booking">Только что созданная бронь (ещё в транзакции создания).</param>
    /// <param name="createdByBarber">
    /// <c>true</c> — бронь внёс барбер (<c>CustomerId</c> задан или передан гость);
    /// <c>false</c> — клиент записался сам.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default);
}
