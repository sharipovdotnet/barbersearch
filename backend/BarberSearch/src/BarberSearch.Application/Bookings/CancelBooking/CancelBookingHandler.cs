using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CancelBooking;

/// <summary>
/// Обработчик команды <see cref="CancelBookingCommand"/>.
/// </summary>
public class CancelBookingHandler : ICommandHandler<CancelBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CancelBookingHandler> _logger;

    public CancelBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        ILogger<CancelBookingHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
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

        if (booking.UserId != currentUserId)
        {
            var allowed = await _currentUser.EnsureIsBarberOfAsync(
                _barberProfiles,
                booking.BarberProfileId,
                cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        var cancelled = booking.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been cancelled.", booking.Id);

        return UnitResult.Success<Failure>();
    }
}
