using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.ConfirmBooking;

/// <summary>
/// Команда подтверждения брони барбером.
/// </summary>
public record ConfirmBookingCommand(Guid BookingId) : ICommand;
