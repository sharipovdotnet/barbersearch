using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Bookings.BookingChangeEmail;
using BarberSearch.Application.Common;
using BarberSearch.Application.Push.BarberBooking;
using BarberSearch.Application.Push.BookingStatus;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CancelBooking;

/// <summary>
/// Обработчик команды <see cref="CancelBookingCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Уведомление выбирается по <b>поверхности</b>, а не по автору. Отмена клиентом в мобильном
/// приложении push не создаёт — экран уже подтвердил действие, — но ставит письмо на
/// подтверждённый адрес. Отмена клиентом с веба и отмена барбером или администратором
/// создают push всегда; молчание здесь означало бы, что клиент не узнает об отмене
/// (AGENT.md §20.11).
/// </para>
/// <para>
/// Вид уведомления выбирается по <c>isClientOwner</c>: хендлер вызывает уведомитель всегда,
/// а гейт поверхности живёт в нём. Сбой постановки в очередь или письма логируется
/// предупреждением и не меняет результат команды.
/// </para>
/// </remarks>
public class CancelBookingHandler : ICommandHandler<CancelBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly IBookingStatusPushNotifier _pushNotifier;
    private readonly IBarberBookingPushNotifier _barberPushNotifier;
    private readonly IBookingChangeEmailNotifier _emailNotifier;
    private readonly ILogger<CancelBookingHandler> _logger;

    public CancelBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        IBookingStatusPushNotifier pushNotifier,
        IBarberBookingPushNotifier barberPushNotifier,
        IBookingChangeEmailNotifier emailNotifier,
        ILogger<CancelBookingHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
        _pushNotifier = pushNotifier;
        _barberPushNotifier = barberPushNotifier;
        _emailNotifier = emailNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, отменяет её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        CancelBookingCommand command,
        CancellationToken cancellationToken)
    {
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var currentUserId = userIdResult.Value;

        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        // У гостевой брони (walk-in) клиента-владельца нет: UserId = null, поэтому отменить
        // её может только барбер профиля или администратор — как и раньше.
        var isClientOwner = booking.UserId is { } ownerUserId && ownerUserId == currentUserId;
        var isBarberCancellation = false;

        if (!isClientOwner)
        {
            var allowed = await _currentUser.EnsureIsBarberOfAsync(
                _barberProfiles,
                booking.BarberProfileId,
                cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }

            // Администратор отменяет со своего стола поддержки и причину не пишет:
            // обязательность относится именно к барберу, который отменяет свою запись.
            isBarberCancellation = !_currentUser.IsAdmin;
        }

        if (isBarberCancellation && string.IsNullOrWhiteSpace(command.CancellationReason))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.CancelReasonRequired,
                "A barber must provide a cancellation reason.",
                nameof(command.CancellationReason)));
        }

        var cancelled = booking.Cancel(command.CancellationReason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Booking {BookingId} has been cancelled by barber: {IsBarberCancellation}.",
            booking.Id,
            isBarberCancellation);

        // Уведомитель вызывается всегда: молчание — свойство поверхности, а не автора.
        // Клиентская отмена из приложения гаснет в уведомителе, а с веба — уходит push'ем.
        // Гостевой брони и адресата может не быть: оба уведомителя вернут успешный no-op.
        var change = isClientOwner
            ? BookingStatusChange.CancelledByCustomer
            : BookingStatusChange.CancelledByBarber;

        await BookingStatusPush.NotifyAsync(
            _pushNotifier,
            _logger,
            booking,
            change,
            cancellationToken);

        // Мастеру — отдельное событие: клиент отменил запись или её снял администратор.
        // Собственную отмену мастер не получает: он и есть автор действия. Гостевая бронь
        // клиента-пользователя не имеет, но получатель здесь — владелец профиля.
        await BarberBookingPush.NotifyCancelledAsync(
            _barberPushNotifier,
            _logger,
            booking,
            cancelledByBarber: isBarberCancellation,
            cancellationToken);

        // Письмо заменяет push тогда и только тогда, когда клиент отменил запись в том же
        // приложении; решение целиком живёт в уведомителе (isClientOwner + IsMobileApp).
        await _emailNotifier.NotifyCancelledAsync(booking, isClientOwner, cancellationToken);

        return UnitResult.Success<Failure>();
    }
}
