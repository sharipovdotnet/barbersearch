namespace BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;

/// <summary>
/// Данные запроса на добавление услуги в профиль барбера.
/// </summary>
/// <remarks>
/// Услуга берётся из каталога по <see cref="HairServiceId"/>, а цена и длительность
/// задаются для конкретного барбера: каталог хранит только длительность по умолчанию.
/// </remarks>
public record AddBarberProfileServiceRequest(
    Guid HairServiceId,
    decimal Price,
    int DurationMinutes);
