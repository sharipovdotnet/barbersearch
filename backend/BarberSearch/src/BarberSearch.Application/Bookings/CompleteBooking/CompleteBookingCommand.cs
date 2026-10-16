using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.CompleteBooking;

/// <summary>
/// Команда завершения брони.
/// </summary>
public record CompleteBookingCommand(Guid BookingId) : ICommand;
