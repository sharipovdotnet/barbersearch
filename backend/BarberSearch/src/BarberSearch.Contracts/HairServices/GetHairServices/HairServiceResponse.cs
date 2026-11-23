using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.GetHairServices;

/// <summary>
/// Строка публичного справочника услуг.
/// </summary>
/// <remarks>
/// Ответ — голый JSON-массив без пагинации. Длительность по умолчанию и описание
/// отдаются как есть: фильтру они не нужны, но контракт чтения справочника полный.
/// <c>SpecializationId</c> позволяет клиенту группировать услуги по направлениям:
/// запрос с <c>?specializationId=</c> уже отдаёт только услуги выбранного направления.
/// </remarks>
/// <param name="Id">Идентификатор услуги справочника.</param>
/// <param name="Name">Название для подписи в фильтре.</param>
/// <param name="Category">Категория услуги.</param>
/// <param name="SpecializationId">Направление (специализация), которому принадлежит услуга.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record HairServiceResponse(
    Guid Id,
    string Name,
    ServiceCategory Category,
    Guid SpecializationId,
    int DefaultDurationMinutes,
    string? Description);
