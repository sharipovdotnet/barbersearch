using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Раскладывает плоский справочник специализаций по категориям — так же, как это делает веб.
/// </summary>
/// <remarks>
/// <para>
/// Эталон — <c>groupSpecializations</c> из <c>frontend/src/lib/specialization-groups.ts</c>:
/// порядок групп каталожный (<see cref="CategoryOrder"/>), а не алфавитный, внутри группы
/// сохраняется порядок ответа API (сервер отдаёт по имени), пустые категории не возвращаются.
/// Заголовки берутся из <see cref="BarberProfileText.CategoryText"/> — того же словаря, что
/// и в кабинете мастера, поэтому подписи совпадают с вебом и переводятся вместе с интерфейсом.
/// </para>
/// <para>
/// Функция чистая: входной список и его элементы не меняются, поэтому один и тот же ответ API
/// можно раскладывать повторно без запроса в сеть.
/// </para>
/// </remarks>
public static class SearchSpecializationGroups
{
    /// <summary>
    /// Порядок категорий в селекте: как в каталоге, от стрижки к «Другому».
    /// </summary>
    /// <remarks>
    /// Совпадает с <c>SERVICE_CATEGORY_ORDER</c> веба и с порядком объявления
    /// <see cref="ServiceCategory"/>: один список на оба клиента, чтобы фильтр и каталог
    /// читались сверху вниз одинаково.
    /// </remarks>
    public static readonly IReadOnlyList<ServiceCategory> CategoryOrder =
    [
        ServiceCategory.Haircut,
        ServiceCategory.Beard,
        ServiceCategory.Coloring,
        ServiceCategory.Styling,
        ServiceCategory.HairTreatment,
        ServiceCategory.Other,
    ];

    /// <summary>
    /// Строит группы специализаций для сгруппированного списка.
    /// </summary>
    /// <param name="specializations">Специализации справочника в порядке ответа API.</param>
    /// <returns>Непустые группы в каталожном порядке категорий.</returns>
    public static IReadOnlyList<SearchSpecializationGroup> Build(
        IReadOnlyList<SpecializationResponse> specializations)
    {
        ArgumentNullException.ThrowIfNull(specializations);

        var byCategory = new Dictionary<ServiceCategory, List<SearchCatalogOption>>();

        foreach (var specialization in specializations)
        {
            if (!byCategory.TryGetValue(specialization.Category, out var group))
            {
                group = [];
                byCategory.Add(specialization.Category, group);
            }

            group.Add(SearchCatalogOption.From(specialization.Id, specialization.Name));
        }

        var groups = new List<SearchSpecializationGroup>(byCategory.Count);

        foreach (var category in CategoryOrder)
        {
            if (byCategory.TryGetValue(category, out var group))
            {
                groups.Add(new SearchSpecializationGroup(
                    category,
                    BarberProfileText.CategoryText(category),
                    group));
            }
        }

        return groups;
    }
}
