using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.Specializations;

/// <summary>
/// Число активных услуг направления в одной категории — строка группового запроса
/// <c>GROUP BY specialization_id, category</c>.
/// </summary>
/// <remarks>
/// Счётчики читаются одним запросом на весь список направлений: иначе категория каждого
/// направления стоила бы отдельного обращения к базе (N+1). Учитываются только включённые
/// услуги — выключенная услуга не участвует в выборе категории направления.
/// </remarks>
/// <param name="SpecializationId">Направление (специализация), которому принадлежат услуги.</param>
/// <param name="Category">Категория услуг.</param>
/// <param name="Count">Сколько активных услуг этой категории у направления.</param>
public record ServiceCategoryCount(
    Guid SpecializationId,
    ServiceCategory Category,
    int Count);
