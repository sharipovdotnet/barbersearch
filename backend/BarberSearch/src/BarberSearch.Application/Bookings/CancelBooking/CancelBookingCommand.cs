using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Bookings.CancelBooking;

/// <summary>
/// Команда отмены брони.
/// </summary>
/// <param name="BookingId">Идентификатор брони.</param>
/// <param name="CancellationReason">
/// Причина отмены. Обязательна, когда отменяет барбер; клиент и администратор
/// могут отменить без неё.
/// </param>
public record CancelBookingCommand(Guid BookingId, string? CancellationReason = null) : ICommand;
