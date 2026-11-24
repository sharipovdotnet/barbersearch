using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.GetHairServices;

/// <summary>
/// Строка публичного справочника услуг.
/// </summary>
/// <remarks>
/// Ответ — голый JSON-массив без пагинации. Длительность по умолчанию и описание
/// отдаются как есть: фильтру они не нужны, но контракт чтения справочника полный.
/// </remarks>
/// <param name="Id">Идентификатор услуги справочника.</param>
/// <param name="Name">Название для подписи в фильтре.</param>
/// <param name="Category">Категория услуги.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record HairServiceResponse(
    Guid Id,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description);
