using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.ConfirmBooking;

/// <summary>
/// Обработчик команды <see cref="ConfirmBookingCommand"/>.
/// </summary>
public class ConfirmBookingHandler : ICommandHandler<ConfirmBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ConfirmBookingHandler> _logger;

    public ConfirmBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        ILogger<ConfirmBookingHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, подтверждает её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ConfirmBookingCommand command,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        var allowed = await _currentUser.EnsureIsBarberOfAsync(
            _barberProfiles,
            booking.BarberProfileId,
            cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var confirmed = booking.Confirm();
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been confirmed.", booking.Id);

        return UnitResult.Success<Failure>();
    }
}
