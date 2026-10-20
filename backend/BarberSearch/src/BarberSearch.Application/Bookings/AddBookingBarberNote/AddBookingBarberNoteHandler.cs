using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.AddBookingBarberNote;

/// <summary>
/// Обработчик команды <see cref="AddBookingBarberNoteCommand"/>.
/// </summary>
public class AddBookingBarberNoteHandler : ICommandHandler<AddBookingBarberNoteCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _barberProfiles;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBookingBarberNoteHandler> _logger;

    public AddBookingBarberNoteHandler(
        IBookingRepository repository,
        IBarberProfileRepository barberProfiles,
        ICurrentUser currentUser,
        ILogger<AddBookingBarberNoteHandler> logger)
    {
        _repository = repository;
        _barberProfiles = barberProfiles;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает бронь, добавляет заметку барбера и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBookingBarberNoteCommand command,
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

        var noted = booking.AddBarberNote(command.Note);
        if (noted.IsFailure)
        {
            return noted.Error;
        }

        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Booking {BookingId} barber note has been added.", booking.Id);

        return UnitResult.Success<Failure>();
    }
}
