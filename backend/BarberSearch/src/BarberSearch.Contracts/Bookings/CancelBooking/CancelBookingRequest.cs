namespace BarberSearch.Contracts.Bookings.CancelBooking;

/// <summary>
/// Данные запроса на отмену брони.
/// </summary>
/// <remarks>
/// Тело необязательно: клиент и администратор отменяют запись без причины, поэтому
/// старый вызов без тела продолжает работать. Барбер обязан передать
/// <see cref="Reason"/>, иначе сервер отвечает <c>400 booking.cancel.reasonRequired</c>.
/// </remarks>
/// <param name="Reason">Причина отмены; до <c>500</c> символов.</param>
public record CancelBookingRequest(string? Reason);
