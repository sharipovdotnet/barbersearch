namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Клиент админской брони: имя, email и телефон.
/// </summary>
/// <remarks>
/// <para>
/// Email и телефон — персональные данные, и здесь они допустимы только потому, что чтение
/// закрыто политикой <c>AdminOnly</c>: поддержка ищет бронь по email клиента и объясняет
/// ему детали визита. В публичные контракты (<c>BookingResponse</c>, <c>BarberBookingResponse</c>)
/// эти поля не попадают.
/// </para>
/// <para>
/// Контракт описывает оба вида клиента: аккаунт и гостя (walk-in). У гостя нет
/// <paramref name="Id"/> и <paramref name="Email"/> — они <c>null</c>, а имя, фамилия и телефон
/// берутся из гостевого снимка самой брони. Поэтому админская выдача читает клиента
/// <c>LEFT JOIN</c>, а не внутренним соединением: иначе гостевые строки исчезли бы из списка.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор пользователя-клиента; <c>null</c> у гостя.</param>
/// <param name="FirstName">Имя клиента или гостя.</param>
/// <param name="LastName">Фамилия клиента или гостя; пустая строка, если не указана.</param>
/// <param name="Email">
/// Email клиента в каноническом виде (trim и нижний регистр); <c>null</c> у гостя.
/// </param>
/// <param name="PhoneNumber">Телефон клиента или гостя; <c>null</c> — номер не указан.</param>
/// <param name="AvatarUrl">Аватар клиента; <c>null</c> у гостя и у клиента без фото.</param>
public record AdminBookingCustomerResponse(
    Guid? Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? AvatarUrl);
