using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BookingStatus;

/// <summary>
/// Постановка в очередь push-события о смене статуса брони.
/// </summary>
/// <remarks>
/// <para>
/// Сам путь постановки общий с событием создания и живёт в <see cref="BookingPushEnqueuer"/>:
/// гейт канала, гейт получателя, снимок каталога, доменная фабрика и
/// <c>INSERT … ON CONFLICT DO NOTHING</c>. Здесь остаётся только отображение
/// <see cref="BookingStatusChange"/> в <see cref="PushNotificationKind"/>.
/// </para>
/// <para>
/// Для переноса снимок берётся из уже сохранённой брони, поэтому <c>start_at</c> строки —
/// новое время визита, а не прежнее. Повторный перенос даёт новую строку благодаря
/// <c>start_at</c> в уникальном индексе; перенос на то же самое время — дубль, и
/// <c>TryAddAsync</c> вернёт <c>false</c>.
/// </para>
/// </remarks>
public sealed class BookingStatusPushNotifier : IBookingStatusPushNotifier
{
    private readonly BookingPushEnqueuer _queue;

    public BookingStatusPushNotifier(
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
        BookingStatusChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return _queue.EnqueueAsync(booking, KindOf(change), cancellationToken);
    }

    /// <summary>
    /// Отображает смену статуса в вид уведомления.
    /// </summary>
    private static PushNotificationKind KindOf(BookingStatusChange change)
        => change switch
        {
            BookingStatusChange.Confirmed => PushNotificationKind.BookingConfirmed,
            BookingStatusChange.CancelledByBarber => PushNotificationKind.BookingCancelledByBarber,
            BookingStatusChange.RescheduledByBarber => PushNotificationKind.BookingRescheduledByBarber,
            _ => throw new ArgumentOutOfRangeException(
                nameof(change),
                change,
                "Unsupported booking status change.")
        };
}
