using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Application.Push.BarberBooking;

/// <summary>
/// Толерантный вызов <see cref="IBarberBookingPushNotifier"/> из обработчиков брони.
/// </summary>
/// <remarks>
/// Сбой уведомления мастера не меняет результат уже сохранённой записи или отмены. В журнал
/// попадают только идентификатор брони, вид события и коды ошибок (или тип исключения):
/// имя клиента в лог не пишется (152-ФЗ). Отмена запроса пробрасывается, чтобы запрос не
/// выглядел успешным.
/// </remarks>
internal static class BarberBookingPush
{
    /// <summary>
    /// Ставит событие о новой записи, не роняя создание брони.
    /// </summary>
    internal static async Task NotifyCreatedAsync(
        IBarberBookingPushNotifier notifier,
        ILogger logger,
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken)
    {
        try
        {
            var notified = await notifier.NotifyCreatedAsync(
                booking,
                createdByBarber,
                cancellationToken);
            if (notified.IsFailure)
            {
                logger.LogWarning(
                    "Booking {BookingId} was created, but the barber push notification was not enqueued: {Codes}.",
                    booking.Id,
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
            logger.LogWarning(
                "Booking {BookingId} was created, but the barber push notification failed: {ErrorType}.",
                booking.Id,
                exception.GetType().Name);
        }
    }

    /// <summary>
    /// Ставит событие об отмене, не роняя саму отмену.
    /// </summary>
    internal static async Task NotifyCancelledAsync(
        IBarberBookingPushNotifier notifier,
        ILogger logger,
        Booking booking,
        bool cancelledByBarber,
        CancellationToken cancellationToken)
    {
        try
        {
            var notified = await notifier.NotifyCancelledAsync(
                booking,
                cancelledByBarber,
                cancellationToken);
            if (notified.IsFailure)
            {
                logger.LogWarning(
                    "Booking {BookingId} was cancelled, but the barber push notification was not enqueued: {Codes}.",
                    booking.Id,
                    string.Join(", ", notified.Error.Errors.Select(error => error.Code)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Сбой push не должен откатывать уже сохранённую отмену.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                "Booking {BookingId} was cancelled, but the barber push notification failed: {ErrorType}.",
                booking.Id,
                exception.GetType().Name);
        }
    }
}
