namespace BarberSearch.Contracts.Bookings.CreateBooking;

/// <summary>
/// Данные запроса на создание брони.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateBookingCommand</c> несёт его целиком,
/// поэтому четыре подряд идущих идентификатора невозможно перепутать местами при передаче
/// в <c>Booking.Create</c>. Ключ идемпотентности обязателен: при обрыве сети мобильный клиент
/// повторяет запрос, и повтор с тем же ключом должен вернуть уже созданную бронь
/// (см. AGENT.md §15.5).
/// </remarks>
public record CreateBookingRequest(
    Guid UserId,
    Guid BarberProfileId,
    Guid BarberServiceId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal Price,
    string IdempotencyKey,
    string? CustomerNote);
