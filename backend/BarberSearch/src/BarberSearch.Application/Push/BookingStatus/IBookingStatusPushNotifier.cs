using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BookingStatus;

/// <summary>
/// Ставит в очередь push-событие о смене статуса брони, выполненной сотрудником салона.
/// </summary>
/// <remarks>
/// <para>
/// Вызывается обработчиками <c>ConfirmBookingHandler</c>, <c>CancelBookingHandler</c> и
/// <c>RescheduleBookingHandler</c> после успешного сохранения брони, в том же scope запроса.
/// Строка очереди и переход статуса <b>не</b> одна транзакция: подтверждение и отмена
/// сохраняются до вызова, а перенос ставит событие после коммита — ошибка вставки в
/// push-очередь в PostgreSQL иначе сделала бы транзакцию недействительной. Воркер
/// <c>PushDispatchService</c> доставит событие на следующем тике (≤ <c>Push:IntervalSeconds</c>).
/// </para>
/// <para>
/// Это no-op, когда канал выключен (<c>Push:Enabled=false</c>) или у брони нет получателя —
/// гостевая запись (<c>Booking.UserId == null</c>). Действие самого клиента сюда не приходит:
/// обработчики вызывают уведомитель только для барбера и администратора.
/// </para>
/// <para>
/// Ошибка постановки в очередь не должна откатывать уже сохранённую бронь: вызывающий
/// обязан проглотить <see cref="Failure"/> и только записать предупреждение с идентификатором
/// брони и кодами ошибок. Никаких персональных данных и текста сообщения в журнал не
/// попадает (152-ФЗ, AGENT.md §20.11).
/// </para>
/// </remarks>
public interface IBookingStatusPushNotifier
{
    /// <summary>
    /// Создаёт строку push-очереди о смене статуса, если канал включён и у брони есть клиент.
    /// </summary>
    /// <param name="booking">Бронь после сохранения перехода; её <c>StartAt</c> — время события.</param>
    /// <param name="change">Что именно произошло: подтверждение, отмена или перенос.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        BookingStatusChange change,
        CancellationToken cancellationToken = default);
}
