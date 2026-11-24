using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.HairServices;

/// <summary>
/// Строка публичного справочника услуг: услуга и категория её специализации.
/// </summary>
/// <remarks>
/// Категория живёт у специализации, а не у услуги, поэтому чтения справочника джойнят
/// <c>specializations</c> и возвращают пару. Это запись чтения, а не навигация в домене:
/// специализация — другой агрегат, и ссылка на него остаётся идентификатором.
/// </remarks>
/// <param name="HairService">Услуга справочника.</param>
/// <param name="Category">Категория специализации услуги.</param>
public sealed record HairServiceCatalogRow(HairService HairService, ServiceCategory Category);
