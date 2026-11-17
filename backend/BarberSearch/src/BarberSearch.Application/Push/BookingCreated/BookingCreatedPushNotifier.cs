using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BookingCreated;

/// <summary>
/// Постановка в очередь push-события о создании брони.
/// </summary>
/// <remarks>
/// <para>
/// Дешёвый путь без сети: один запрос снимка каталога и один <c>INSERT … ON CONFLICT DO NOTHING</c>
/// в той же транзакции, что и бронь. Текст не собирается — это делает воркер доставки
/// (<c>PushMessages.For</c>), поэтому в момент создания нужен только снимок.
/// </para>
/// <para>
/// Событие создания не отменяет напоминание: уникальность в базе — по паре
/// <c>(booking_id, kind)</c>, поэтому у одной брони живут оба вида уведомлений.
/// </para>
/// </remarks>
public sealed class BookingCreatedPushNotifier : IBookingCreatedPushNotifier
{
    private readonly IPushSourceRepository _source;
    private readonly IPushNotificationRepository _notifications;
    private readonly PushOptions _options;

    public BookingCreatedPushNotifier(
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

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        bool createdByBarber,
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

        var kind = createdByBarber
            ? PushNotificationKind.BookingCreatedByBarber
            : PushNotificationKind.BookingCreatedByCustomer;

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
