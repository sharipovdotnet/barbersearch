using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Услуга барбера в карточке: конкретная цена и длительность у этого мастера.
/// </summary>
/// <param name="Id">Идентификатор записи «услуга барбера».</param>
/// <param name="HairServiceId">Идентификатор услуги справочника.</param>
/// <param name="Name">Название услуги справочника.</param>
/// <param name="Category">Категория услуги справочника.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию из справочника.</param>
/// <param name="Price">Цена у этого барбера.</param>
/// <param name="DurationMinutes">Длительность у этого барбера.</param>
/// <param name="IsActive">Можно ли записаться на услугу.</param>
public record BarberProfileServiceResponse(
    Guid Id,
    Guid HairServiceId,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    decimal Price,
    int DurationMinutes,
    bool IsActive);
