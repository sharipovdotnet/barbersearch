namespace BarberSearch.Contracts.Bookings.CreateBarberBooking;

/// <summary>
/// Данные записи, которую барбер создаёт для клиента.
/// </summary>
/// <remarks>
/// <para>
/// Профиля барбера в теле нет: он берётся из claim'а <c>barber_profile_id</c> сессии,
/// поэтому записать клиента к чужому мастеру нельзя. Цена не передаётся: её, как и в публичной
/// записи, определяет <c>BarberService.Price</c> (R-33). Бронь создаётся сразу
/// в статусе <c>Confirmed</c> — барбер и есть подтверждающая сторона.
/// </para>
/// <para>
/// Запись создаётся либо для зарегистрированного клиента (<paramref name="CustomerId"/>),
/// либо для гостя (walk-in) — по имени и телефону, без создания аккаунта. Одновременно
/// клиент и гостевые поля не передаются: иначе <c>400 booking.create.customerAmbiguous</c>.
/// Гость обязателен именем и телефоном (<c>booking.create.guestFirstName</c> /
/// <c>booking.create.guestPhone</c>), фамилия необязательна. Если не задано ни то, ни другое —
/// <c>400 booking.create.customer</c>. Email у гостя нет: домен его не хранит.
/// </para>
/// </remarks>
/// <param name="CustomerId">
/// Идентификатор активного пользователя, найденного через <c>GET api/barber/clients</c>;
/// роль не важна — записать можно клиента, администратора, барбера и т. д.
/// <c>null</c> — запись создаётся для гостя.
/// </param>
/// <param name="GuestFirstName">Имя гостя; обязательно, если <paramref name="CustomerId"/> = <c>null</c>.</param>
/// <param name="GuestLastName">Фамилия гостя; необязательна.</param>
/// <param name="GuestPhone">Телефон гостя; обязателен, если <paramref name="CustomerId"/> = <c>null</c>.</param>
/// <param name="BarberServiceId">Идентификатор услуги барбера (<c>barber_services.id</c>).</param>
/// <param name="SalonId">Идентификатор салона, в котором работает барбер.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="IdempotencyKey">Ключ идемпотентности: повтор возвращает ту же бронь.</param>
/// <param name="CustomerNote">Комментарий клиента либо <c>null</c>.</param>
public record CreateBarberBookingRequest(
    Guid? CustomerId,
    string? GuestFirstName,
    string? GuestLastName,
    string? GuestPhone,
    Guid BarberServiceId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string IdempotencyKey,
    string? CustomerNote);
