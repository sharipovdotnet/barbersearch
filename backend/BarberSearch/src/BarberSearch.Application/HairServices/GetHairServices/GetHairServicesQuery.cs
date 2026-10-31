using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.HairServices.GetHairServices;

namespace BarberSearch.Application.HairServices.GetHairServices;

/// <summary>
/// Публичный справочник активных услуг для фильтров витрины.
/// </summary>
/// <remarks>
/// Параметров нет: справочник маленький, постраничная выдача ему не нужна.
/// </remarks>
public record GetHairServicesQuery : IQuery<IReadOnlyList<HairServiceResponse>>;
