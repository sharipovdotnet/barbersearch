using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.Specializations;

/// <summary>
/// Выводит категорию направления из категорий его активных услуг.
/// </summary>
/// <remarks>
/// <para>
/// Отдельного поля у <c>specializations</c> нет: категория — это агрегат по услугам
/// направления, и правило должно жить в одном месте для публичного и админского чтения.
/// </para>
/// <para>
/// Правило: побеждает категория большинства активных услуг. Ничья решается меньшим числовым
/// значением <see cref="ServiceCategory"/> (<c>Haircut = 1</c>, …, <c>Other = 99</c>), поэтому
/// результат не зависит от порядка строк в ответе базы. Направление без активных услуг
/// получает <see cref="ServiceCategory.Other"/>.
/// </para>
/// </remarks>
public static class SpecializationCategoryResolver
{
    /// <summary>
    /// Категория одного направления по его счётчикам.
    /// </summary>
    /// <param name="counts">
    /// Счётчики активных услуг этого направления; пустая последовательность — услуг нет.
    /// </param>
    /// <returns>Категория большинства, при ничьей — с меньшим кодом, иначе <c>Other</c>.</returns>
    public static ServiceCategory Resolve(IEnumerable<ServiceCategoryCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return counts
            .OrderByDescending(count => count.Count)
            .ThenBy(count => (int)count.Category)
            .Select(count => count.Category)
            .DefaultIfEmpty(ServiceCategory.Other)
            .First();
    }

    /// <summary>
    /// Категория каждого из перечисленных направлений — одной словарной картой.
    /// </summary>
    /// <remarks>
    /// Направление без счётчиков (нет активных услуг) получает <c>Other</c>, поэтому вызывающий
    /// не повторяет fallback: он просто берёт значение по идентификатору. Счётчики направлений,
    /// которых нет в <paramref name="specializationIds"/>, в результат не попадают.
    /// </remarks>
    /// <param name="specializationIds">Направления, для которых нужна категория.</param>
    /// <param name="counts">Счётчики по активным услугам всего списка.</param>
    /// <returns>Категория для каждого идентификатора; порядок результата не контрактен.</returns>
    public static IReadOnlyDictionary<Guid, ServiceCategory> ResolveAll(
        IEnumerable<Guid> specializationIds,
        IEnumerable<ServiceCategoryCount> counts)
    {
        ArgumentNullException.ThrowIfNull(specializationIds);
        ArgumentNullException.ThrowIfNull(counts);

        var bySpecialization = counts
            .GroupBy(count => count.SpecializationId)
            .ToDictionary(group => group.Key, group => Resolve(group));

        return specializationIds
            .Distinct()
            .ToDictionary(
                id => id,
                id => bySpecialization.GetValueOrDefault(id, ServiceCategory.Other));
    }
}
