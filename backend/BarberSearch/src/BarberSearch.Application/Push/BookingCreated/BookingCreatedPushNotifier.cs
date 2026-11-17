using BarberSearch.Application.Abstractions;
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
/// <c>INSERT … ON CONFLICT DO NOTHING</c>. Здесь остаётся выбор вида и гейт поверхности.
/// </para>
/// <para>
/// Клиент, записавшийся <b>в приложении</b>, push не получает: экран уже подтвердил бронь,
/// а сообщение пришло бы после действия. Запись с другой поверхности (веб, кабинет, инструмент
/// без заголовка) уведомляет клиента. Создание барбером не гейтится никогда: о ручной записи
/// клиент не знал (AGENT.md §20.11).
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
    private readonly IClientSurface _clientSurface;

    public BookingCreatedPushNotifier(
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
        bool createdByBarber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Действие в том же приложении, которое получило бы push, уведомления не создаёт.
        // Барберское создание не гейтится ни при каком заголовке.
        if (_clientSurface.IsMobileApp && !createdByBarber)
        {
            return Task.FromResult(UnitResult.Success<Failure>());
        }

        var kind = createdByBarber
            ? PushNotificationKind.BookingCreatedByBarber
            : PushNotificationKind.BookingCreatedByCustomer;

        return _queue.EnqueueAsync(booking, kind, cancellationToken);
    }
}
