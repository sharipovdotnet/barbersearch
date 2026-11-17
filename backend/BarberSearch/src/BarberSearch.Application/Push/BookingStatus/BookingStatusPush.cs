using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Application.Push.BookingStatus;

/// <summary>
/// Толерантный вызов <see cref="IBookingStatusPushNotifier"/> из обработчиков брони.
/// </summary>
/// <remarks>
/// <para>
/// Один и тот же контракт у подтверждения, отмены и переноса: уведомление ставится после
/// успешного сохранения, а его сбой не меняет результат команды. Дублировать обработку
/// ошибок трижды значило бы дать трём путям разойтись в том, что попадает в журнал, поэтому
/// она живёт здесь — по образцу <c>CreateBookingHandler.NotifyBookingCreatedAsync</c>.
/// </para>
/// <para>
/// В журнал попадают только идентификатор брони и коды ошибок (или тип исключения):
/// ни имени, ни телефона, ни заметки клиента, ни текста сообщения (152-ФЗ). Отмена запроса
/// пробрасывается — отменённый запрос не должен выглядеть успешным, — а исключение
/// транспорта/базы не выходит наружу: бронь к этому моменту уже сохранена.
/// </para>
/// </remarks>
internal static class BookingStatusPush
{
    /// <summary>
    /// Ставит push-событие о смене статуса, не роняя саму команду.
    /// </summary>
    internal static async Task NotifyAsync(
        IBookingStatusPushNotifier notifier,
        ILogger logger,
        Booking booking,
        BookingStatusChange change,
        CancellationToken cancellationToken)
    {
        try
        {
            var notified = await notifier.NotifyAsync(booking, change, cancellationToken);
            if (notified.IsFailure)
            {
                logger.LogWarning(
                    "Booking {BookingId} changed to {Change}, but its push notification was not enqueued: {Codes}.",
                    booking.Id,
                    change,
                    string.Join(", ", notified.Error.Errors.Select(error => error.Code)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Сбой push не должен откатывать уже сохранённую бронь.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Тип, а не текст исключения: сообщение может содержать параметры SQL-запроса.
            logger.LogWarning(
                "Booking {BookingId} changed to {Change}, but its push notification failed: {ErrorType}.",
                booking.Id,
                change,
                exception.GetType().Name);
        }
    }
}
