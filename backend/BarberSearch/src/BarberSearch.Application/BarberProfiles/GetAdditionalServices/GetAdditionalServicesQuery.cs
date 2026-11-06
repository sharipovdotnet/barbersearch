using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.GetAdditionalServices;

/// <summary>
/// Подсказки фильтра «дополнительные услуги» на витрине.
/// </summary>
/// <remarks>
/// Параметров нет: клиент получает до 100 различных значений из видимых профилей
/// и использует их как подсказки свободного поля, а не как справочник.
/// </remarks>
public record GetAdditionalServicesQuery : IQuery<IReadOnlyList<string>>;
