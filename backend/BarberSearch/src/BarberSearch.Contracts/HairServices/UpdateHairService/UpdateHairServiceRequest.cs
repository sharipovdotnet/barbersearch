namespace BarberSearch.Contracts.HairServices.UpdateHairService;

/// <summary>
/// Данные запроса на изменение услуги каталога.
/// </summary>
/// <remarks>
/// Категории здесь нет: она не меняется, потому что цена и длительность конкретных
/// барберов привязаны к услуге, а перевод услуги в другую категорию менял бы смысл
/// уже сделанных предложений.
/// </remarks>
public record UpdateHairServiceRequest(
    string Name,
    string? Description,
    int DefaultDurationMinutes);
