namespace BarberSearch.Contracts.Bookings.CreateBarberBooking;

/// <summary>
/// Данные записи, которую барбер создаёт для клиента.
/// </summary>
/// <remarks>
/// Профиля барбера в теле нет: он берётся из claim'а <c>barber_profile_id</c> сессии,
/// поэтому записать клиента к чужому мастеру нельзя. <c>CustomerId</c> — идентификатор
/// зарегистрированного клиента, найденного через <c>GET api/barber/clients</c>; роль
/// и активность проверяет Application. Цена не передаётся: её, как и в публичной
/// записи, определяет <c>BarberService.Price</c> (R-33). Бронь создаётся сразу
/// в статусе <c>Confirmed</c> — барбер и есть подтверждающая сторона.
/// </remarks>
/// <param name="CustomerId">Идентификатор клиента, для которого создаётся запись.</param>
/// <param name="BarberServiceId">Идентификатор услуги барбера (<c>barber_services.id</c>).</param>
/// <param name="SalonId">Идентификатор салона, в котором работает барбер.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="IdempotencyKey">Ключ идемпотентности: повтор возвращает ту же бронь.</param>
/// <param name="CustomerNote">Комментарий клиента либо <c>null</c>.</param>
public record CreateBarberBookingRequest(
    Guid CustomerId,
    Guid BarberServiceId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string IdempotencyKey,
    string? CustomerNote);
