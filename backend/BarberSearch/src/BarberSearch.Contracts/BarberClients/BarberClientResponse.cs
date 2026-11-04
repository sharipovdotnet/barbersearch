namespace BarberSearch.Contracts.BarberClients;

/// <summary>
/// Найденный клиент для ручной записи барбером: имя, email и телефон.
/// </summary>
/// <remarks>
/// Контакты — персональные данные, поэтому ответ доступен только роли <c>Barber</c>
/// и только по явному поисковому запросу. Список ограничен десятью строками и
/// не поддерживает пагинацию: это подсказка в форме, а не справочник.
/// </remarks>
/// <param name="Id">Идентификатор пользователя-клиента.</param>
/// <param name="FirstName">Имя.</param>
/// <param name="LastName">Фамилия.</param>
/// <param name="Email">Адрес электронной почты.</param>
/// <param name="PhoneNumber">Телефон либо <c>null</c>, если он не заполнен.</param>
public record BarberClientResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber);
