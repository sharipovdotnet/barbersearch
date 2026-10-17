namespace BarberSearch.Contracts.Salons.UpdateSalonContactInfo;

/// <summary>
/// Данные запроса на изменение контактных данных салона.
/// </summary>
/// <remarks>
/// Оба поля nullable: контакт можно не указывать или очистить. Название салона здесь
/// не меняется — это отдельный сценарий.
/// </remarks>
public record UpdateSalonContactInfoRequest(
    string? PhoneNumber,
    string? WebsiteUrl);
