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
/// Сам путь постановки общий с событиями статуса и живёт в <see cref="BookingPushEnqueuer"/>:
/// гейт канала, гейт получателя, снимок каталога, доменная фабрика и
/// <c>INSERT … ON CONFLICT DO NOTHING</c>. Здесь остаётся только выбор вида.
/// </para>
/// <para>
/// Событие создания не отменяет напоминание и события статуса: уникальность в базе — по
/// тройке <c>(booking_id, kind, start_at)</c>, поэтому у одной брони живут все виды
/// уведомлений.
/// </para>
/// </remarks>
public sealed class BookingCreatedPushNotifier : IBookingCreatedPushNotifier
{
    private readonly BookingPushEnqueuer _queue;

    public BookingCreatedPushNotifier(
        IPushSourceRepository source,
        IPushNotificationRepository notifications,
        PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);

        _queue = new BookingPushEnqueuer(source, notifications, options);
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var kind = createdByBarber
            ? PushNotificationKind.BookingCreatedByBarber
            : PushNotificationKind.BookingCreatedByCustomer;

        return _queue.EnqueueAsync(booking, kind, cancellationToken);
    }
}
