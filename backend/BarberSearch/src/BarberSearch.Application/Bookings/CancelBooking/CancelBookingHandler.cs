using BarberSearch.Application.Abstractions;
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
    private readonly ILogger<CancelBookingHandler> _logger;

    public CancelBookingHandler(IBookingRepository repository, ILogger<CancelBookingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, отменяет её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        CancelBookingCommand command,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

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
