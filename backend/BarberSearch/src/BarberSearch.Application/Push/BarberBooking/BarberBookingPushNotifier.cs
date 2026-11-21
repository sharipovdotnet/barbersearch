using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Push;
using BarberSearch.Application.Users;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.BarberBooking;

/// <summary>
/// Постановка в очередь событий мастера о новой записи и её отмене.
/// </summary>
/// <remarks>
/// <para>
/// Общий путь постановки — <see cref="BookingPushEnqueuer"/>: гейт канала, снимок каталога,
/// доменная фабрика и дедуплицирующая вставка. Здесь только выбор получателя, имени клиента
/// и вида события.
/// </para>
/// <para>
/// Имя клиента — снимок: у зарегистрированного клиента берутся имя и фамилия пользователя,
/// у гостя — поля брони. Телефон и заметка не читаются вовсе, поэтому в push их не попасть.
/// Если пользователь успел исчезнуть, событие всё равно создаётся с деградированным
/// заголовком: «Новая запись» без имени лучше, чем потерянное уведомление.
/// </para>
/// </remarks>
public sealed class BarberBookingPushNotifier : IBarberBookingPushNotifier
{
    private readonly BookingPushEnqueuer _queue;
    private readonly PushOptions _options;
    private readonly IBarberProfileRepository _profiles;
    private readonly IUserRepository _users;

    public BarberBookingPushNotifier(
        IPushSourceRepository source,
        IPushNotificationRepository notifications,
        PushOptions options,
        IBarberProfileRepository profiles,
        IUserRepository users)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(users);

        _queue = new BookingPushEnqueuer(source, notifications, options);
        _options = options;
        _profiles = profiles;
        _users = users;
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> NotifyCreatedAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Мастер внёс запись сам: push о собственном действии ему не нужен.
        if (createdByBarber)
        {
            return Task.FromResult(UnitResult.Success<Failure>());
        }

        return NotifyAsync(
            booking,
            PushNotificationKind.BarberBookingCreated,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> NotifyCancelledAsync(
        Booking booking,
        bool cancelledByBarber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Мастер отменил запись сам: второй раз сообщать ему нечего.
        if (cancelledByBarber)
        {
            return Task.FromResult(UnitResult.Success<Failure>());
        }

        return NotifyAsync(
            booking,
            PushNotificationKind.BarberBookingCancelled,
            cancellationToken);
    }

    /// <summary>
    /// Общий путь обоих событий: получатель, имя клиента и постановка в очередь.
    /// </summary>
    private async Task<UnitResult<Failure>> NotifyAsync(
        Booking booking,
        PushNotificationKind kind,
        CancellationToken cancellationToken)
    {
        // Выключенный канал не должен даже читать владельца и имя клиента: включение —
        // решение эксплуатации, а не повод для лишних запросов на каждую бронь.
        if (!_options.Enabled)
        {
            return UnitResult.Success<Failure>();
        }

        var ownerResult = await _profiles.GetOwnerIdAsync(
            booking.BarberProfileId,
            cancellationToken);
        if (ownerResult.IsFailure)
        {
            return ownerResult.Error;
        }

        var barberUserId = ownerResult.Value;

        // Клиент и мастер — один и тот же человек (мастер записал себя сам): событие
        // «клиент записался» или «клиент отменил» было бы ложью.
        if (booking.UserId == barberUserId)
        {
            return UnitResult.Success<Failure>();
        }

        var customerName = await ResolveCustomerNameAsync(booking, cancellationToken);

        return await _queue.EnqueueAsync(
            booking,
            kind,
            barberUserId,
            customerName,
            cancellationToken);
    }

    /// <summary>
    /// Собирает имя клиента из аккаунта или гостевых полей брони; <c>null</c> — имени нет.
    /// </summary>
    private async Task<string?> ResolveCustomerNameAsync(
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (booking.UserId is { } userId)
        {
            var userResult = await _users.GetByIdAsync(userId, cancellationToken);

            if (userResult.IsSuccess)
            {
                return ComposeName(userResult.Value.FirstName, userResult.Value.LastName);
            }
        }

        return ComposeName(booking.GuestFirstName, booking.GuestLastName);
    }

    /// <summary>
    /// Склеивает непустые части имени и укладывает результат в предел колонки.
    /// </summary>
    private static string? ComposeName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();

        if (parts.Length == 0)
        {
            return null;
        }

        var name = string.Join(' ', parts);

        return name.Length <= Constants.MAX_NOTIFICATION_CUSTOMER_NAME_LENGTH
            ? name
            : name[..Constants.MAX_NOTIFICATION_CUSTOMER_NAME_LENGTH];
    }
}
