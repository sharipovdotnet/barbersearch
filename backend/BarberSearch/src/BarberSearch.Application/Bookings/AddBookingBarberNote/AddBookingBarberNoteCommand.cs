using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.AddBookingBarberNote;

/// <summary>
/// Команда добавления заметки барбера к брони.
/// </summary>
public record AddBookingBarberNoteCommand(Guid BookingId, string Note) : ICommand;
