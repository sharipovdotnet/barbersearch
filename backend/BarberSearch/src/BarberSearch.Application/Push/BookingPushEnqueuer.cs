using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push;

/// <summary>
/// Общая постановка push-события о брони в очередь: гейты канала и получателя, снимок
/// каталога, доменная фабрика и дедуплицирующая вставка.
/// </summary>
/// <remarks>
/// <para>
/// Один и тот же путь используют событие создания (<c>BookingCreatedPushNotifier</c> в
/// транзакции брони) и события статуса (<c>BookingStatusPushNotifier</c> после сохранения
/// перехода). Дешёвый путь без сети: один запрос снимка каталога и один
/// <c>INSERT … ON CONFLICT DO NOTHING</c>; текст не собирается — это делает воркер доставки
/// (<see cref="PushMessages.For"/>), поэтому в момент события нужен только снимок.
/// </para>
/// <para>
/// Выключенный канал строк не создаёт: включение — решение эксплуатации (AGENT.md §20.5).
/// Гостю отправлять некуда: у брони нет получателя, и такой вызов — успешный no-op.
/// Отсутствующий или сбойный снимок не мешает записи: сбой возвращается вызывающему как
/// <see cref="Failure"/>, а пустой снимок деградирует текст, как у напоминания. Ошибка
/// постановки в очередь не должна откатывать саму бронь — решение принимает вызывающий.
/// </para>
/// </remarks>
public sealed class BookingPushEnqueuer
{
    private readonly IPushSourceRepository _source;
    private readonly IPushNotificationRepository _notifications;
    private readonly PushOptions _options;

    public BookingPushEnqueuer(
        IPushSourceRepository source,
        IPushNotificationRepository notifications,
        PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);

        _source = source;
        _notifications = notifications;
        _options = options;
    }

    /// <summary>
    /// Ставит в очередь уведомление заданного вида для брони, если канал включён и у брони
    /// есть клиент.
    /// </summary>
    /// <param name="booking">Бронь после сохранения; её <c>StartAt</c> — время события.</param>
    /// <param name="kind">Вид уведомления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> EnqueueAsync(
        Booking booking,
        PushNotificationKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Выключенный канал не создаёт строк: включение — решение эксплуатации (AGENT.md §20.5).
        if (!_options.Enabled)
        {
            return UnitResult.Success<Failure>();
        }

        // Гостю отправлять некуда: у брони нет получателя.
        if (booking.UserId is not { } userId)
        {
            return UnitResult.Success<Failure>();
        }

        var snapshotResult = await _source.GetBookingSnapshotAsync(
            booking.BarberProfileId,
            booking.BarberServiceId,
            booking.SalonId,
            cancellationToken);
        if (snapshotResult.IsFailure)
        {
            return snapshotResult.Error;
        }

        // Историческая строка без любого звена каталога не должна мешать брони: текст
        // деградирует так же, как у напоминания с пустым снимком.
        var snapshot = snapshotResult.Value
            ?? new PushBookingSnapshot(null, null, null, null);

        var notification = PushNotification.Create(
            kind,
            userId,
            booking.Id,
            booking.StartAt,
            snapshot.SalonName,
            snapshot.BarberName,
            snapshot.ServiceName,
            snapshot.SalonTimeZoneId);
        if (notification.IsFailure)
        {
            return notification.Error;
        }

        var added = await _notifications.TryAddAsync(notification.Value, cancellationToken);

        return added.IsFailure ? added.Error : UnitResult.Success<Failure>();
    }
}
