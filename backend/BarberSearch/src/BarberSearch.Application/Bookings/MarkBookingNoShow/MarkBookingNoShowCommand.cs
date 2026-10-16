using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.MarkBookingNoShow;

/// <summary>
/// Команда отметки неявки клиента.
/// </summary>
public record MarkBookingNoShowCommand(Guid BookingId) : ICommand;
