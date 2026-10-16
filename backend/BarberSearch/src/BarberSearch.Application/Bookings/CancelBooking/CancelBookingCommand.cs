using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.CancelBooking;

/// <summary>
/// Команда отмены брони.
/// </summary>
public record CancelBookingCommand(Guid BookingId) : ICommand;
