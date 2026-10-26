using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Услуга барбера в карточке: конкретная цена и длительность у этого мастера.
/// </summary>
/// <remarks>
/// <see cref="BarberServiceId"/> — строка <c>barber_services</c> (её принимает
/// <c>CreateBookingRequest.BarberServiceId</c>), а <see cref="HairServiceId"/> — услуга
/// справочника <c>hair_services</c> (её принимают поиск и слоты). Это разные сущности,
/// и называть первую просто <c>id</c> больше нельзя (R-20).
/// </remarks>
/// <param name="BarberServiceId">Идентификатор записи «услуга барбера».</param>
/// <param name="HairServiceId">Идентификатор услуги справочника.</param>
/// <param name="Name">Название услуги справочника.</param>
/// <param name="Category">Категория услуги справочника.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию из справочника.</param>
/// <param name="Price">Цена у этого барбера.</param>
/// <param name="DurationMinutes">Длительность у этого барбера.</param>
/// <param name="IsActive">Можно ли записаться на услугу.</param>
public record BarberProfileServiceResponse(
    Guid BarberServiceId,
    Guid HairServiceId,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    decimal Price,
    int DurationMinutes,
    bool IsActive);
