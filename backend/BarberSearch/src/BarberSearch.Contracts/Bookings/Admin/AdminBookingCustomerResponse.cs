namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Клиент админской брони: имя, email и телефон.
/// </summary>
/// <remarks>
/// Email и телефон — персональные данные, и здесь они допустимы только потому, что чтение
/// закрыто политикой <c>AdminOnly</c>: поддержка ищет бронь по email клиента и объясняет
/// ему детали визита. В публичные контракты (<c>BookingResponse</c>, <c>BarberBookingResponse</c>)
/// эти поля не попадают.
/// </remarks>
/// <param name="Id">Идентификатор пользователя-клиента.</param>
/// <param name="FirstName">Имя клиента, как оно сохранено в профиле.</param>
/// <param name="LastName">Фамилия клиента, как она сохранена в профиле.</param>
/// <param name="Email">Email клиента в каноническом виде (trim и нижний регистр).</param>
/// <param name="PhoneNumber">Телефон клиента; <c>null</c> — номер не указан.</param>
public record AdminBookingCustomerResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber);
