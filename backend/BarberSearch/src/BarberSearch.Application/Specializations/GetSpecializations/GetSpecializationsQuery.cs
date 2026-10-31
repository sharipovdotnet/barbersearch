using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Specializations.GetSpecializations;

namespace BarberSearch.Application.Specializations.GetSpecializations;

/// <summary>
/// Публичный справочник активных специализаций для фильтров витрины.
/// </summary>
/// <remarks>
/// Параметров нет: справочник маленький, постраничная выдача ему не нужна.
/// </remarks>
public record GetSpecializationsQuery : IQuery<IReadOnlyList<SpecializationResponse>>;
