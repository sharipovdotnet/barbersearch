using BarberSearch.Application.Abstractions;
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
/// <c>INSERT … ON CONFLICT DO NOTHING</c>. Здесь остаётся отображение
/// <see cref="BookingStatusChange"/> в <see cref="PushNotificationKind"/> и гейт поверхности.
/// </para>
/// <para>
/// Молчание — свойство поверхности, а не автора. Клиентские отмена и перенос, сделанные
/// <b>в приложении</b>, push не создают: экран уже подтвердил результат. Те же действия
/// с веба, кабинета или инструмента без заголовка — создают. События сотрудника
/// (подтверждение, отмена и перенос барбером или администратором) уходят всегда, даже с
/// заголовком <c>app</c>: иначе отмена администратором молча исчезла бы (AGENT.md §20.11).
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
    private readonly IClientSurface _clientSurface;

    public BookingStatusPushNotifier(
        IPushSourceRepository source,
        IPushNotificationRepository notifications,
        PushOptions options,
        IClientSurface clientSurface)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSurface);

        _queue = new BookingPushEnqueuer(source, notifications, options);
        _clientSurface = clientSurface;
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        BookingStatusChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Гейт стоит только на клиентских видах: действие сотрудника уведомляет всегда.
        if (_clientSurface.IsMobileApp
            && change is BookingStatusChange.CancelledByCustomer
                or BookingStatusChange.RescheduledByCustomer)
        {
            return Task.FromResult(UnitResult.Success<Failure>());
        }

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
            BookingStatusChange.CancelledByCustomer => PushNotificationKind.BookingCancelledByCustomer,
            BookingStatusChange.RescheduledByCustomer => PushNotificationKind.BookingRescheduledByCustomer,
            _ => throw new ArgumentOutOfRangeException(
                nameof(change),
                change,
                "Unsupported booking status change.")
        };
}
