using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CompleteBooking;

/// <summary>
/// Обработчик команды <see cref="CompleteBookingCommand"/>.
/// </summary>
public class CompleteBookingHandler : ICommandHandler<CompleteBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly ILogger<CompleteBookingHandler> _logger;

    public CompleteBookingHandler(IBookingRepository repository, ILogger<CompleteBookingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, завершает её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        CompleteBookingCommand command,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        var completed = booking.Complete();
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Booking {BookingId} has been completed.", booking.Id);

        return UnitResult.Success<Failure>();
    }
}
