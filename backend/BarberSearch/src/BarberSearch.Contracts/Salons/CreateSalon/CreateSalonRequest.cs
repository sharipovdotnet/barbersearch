namespace BarberSearch.Contracts.Salons.CreateSalon;

/// <summary>
/// Данные запроса на создание салона.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateSalonCommand</c> несёт его целиком, а не
/// десять позиционных параметров, поэтому поля невозможно перепутать местами при передаче
/// в <c>Salon.Create</c>. Обязательность и формат полей проверяет домен — здесь только состав.
/// </remarks>
public record CreateSalonRequest(
    string Name,
    string Address,
    string City,
    string Country,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    string? PostalCode,
    string? PhoneNumber,
    string? WebsiteUrl);
