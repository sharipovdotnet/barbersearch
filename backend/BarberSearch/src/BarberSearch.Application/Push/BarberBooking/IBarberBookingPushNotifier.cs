using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BarberBooking;

/// <summary>
/// Ставит в очередь push-события о брони для мастера: новую запись и отмену.
/// </summary>
/// <remarks>
/// <para>
/// Это вторая сторона уже существующих клиентских событий. Запись создаёт только клиент:
/// ручную запись мастер вносит сам и о ней знает. Отмену мастеру сообщают клиент и
/// администратор; собственную отмену мастер писать себе не должен.
/// </para>
/// <para>
/// Получатель — владелец профиля из брони, а не <see cref="Booking.UserId"/>: у гостевой
/// брони клиента-пользователя нет, но администратор всё равно может её отменить. Текст
/// собирается воркером по снимку; здесь только гейты, снимок каталога и имя клиента.
/// </para>
/// <para>
/// Сбой постановки в очередь не должен откатывать бронь или отмену — решение принимает
/// вызывающий обработчик.
/// </para>
/// </remarks>
public interface IBarberBookingPushNotifier
{
    /// <summary>
    /// Ставит в очередь событие «клиент записался» для мастера.
    /// </summary>
    /// <param name="booking">Только что созданная бронь.</param>
    /// <param name="createdByBarber">
    /// <c>true</c> — бронь внёс сам мастер; такое событие не создаётся.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> NotifyCreatedAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ставит в очередь событие «запись отменена» для мастера.
    /// </summary>
    /// <param name="booking">Отменённая бронь.</param>
    /// <param name="cancelledByBarber">
    /// <c>true</c> — запись отменил сам мастер; такое событие не создаётся.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> NotifyCancelledAsync(
        Booking booking,
        bool cancelledByBarber,
        CancellationToken cancellationToken = default);
}
