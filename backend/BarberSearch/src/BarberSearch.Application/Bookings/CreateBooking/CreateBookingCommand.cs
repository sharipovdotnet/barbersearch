using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.CreateBooking;

namespace BarberSearch.Application.Bookings.CreateBooking;

/// <summary>
/// Команда создания брони.
/// </summary>
/// <remarks>
/// Состав полей описан контрактом <see cref="CreateBookingRequest"/> и здесь не дублируется:
/// команда только помечает операцию, данные приходят одним объектом — так же, как
/// <c>CreateBarberProfileCommand</c> с <c>BarberProfileCreateRequest</c>. Ключ идемпотентности
/// обязателен: при обрыве сети мобильный клиент повторяет запрос, и повтор с тем же ключом
/// должен вернуть уже созданную бронь, а не создать вторую
/// (см. <c>IBookingRepository.GetByIdempotencyKeyAsync</c> и AGENT.md §15.5).
/// </remarks>
public record CreateBookingCommand(CreateBookingRequest Request) : ICommand;
