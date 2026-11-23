using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.HairServices.GetHairServices;

namespace BarberSearch.Application.HairServices.GetHairServices;

/// <summary>
/// Публичный справочник активных услуг для фильтров витрины.
/// </summary>
/// <remarks>
/// Параметров пагинации нет: справочник маленький, постраничная выдача ему не нужна.
/// <c>SpecializationId</c> — двухшаговый выбор на витрине: клиент сначала выбирает
/// направление, затем получает только его услуги. <c>null</c> и <c>Guid.Empty</c> —
/// «без фильтра».
/// </remarks>
/// <param name="SpecializationId">
/// Направление (специализация) или <c>null</c>, если нужен весь активный справочник.
/// </param>
public record GetHairServicesQuery(Guid? SpecializationId = null)
    : IQuery<IReadOnlyList<HairServiceResponse>>;
