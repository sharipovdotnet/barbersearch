namespace BarberSearch.Contracts.BarberRequests.Admin;

/// <summary>
/// Заявитель в администраторском ответе по заявке.
/// </summary>
/// <remarks>
/// Это персональные данные, поэтому они есть только в admin-контракте: заявитель видит
/// свою заявку без вложенного блока, а список и карточка администратора — с именем и email
/// аккаунта.
/// </remarks>
/// <param name="Id">Идентификатор аккаунта заявителя.</param>
/// <param name="FullName">Имя и фамилия; при пустом имени — email.</param>
/// <param name="Email">Адрес электронной почты аккаунта.</param>
public record AdminBarberRequestApplicantResponse(
    Guid Id,
    string FullName,
    string Email);
