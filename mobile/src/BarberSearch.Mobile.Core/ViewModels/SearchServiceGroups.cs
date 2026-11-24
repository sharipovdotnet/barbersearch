using System.Globalization;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Раскладывает плоский справочник услуг по специализациям — так же, как это делает веб.
/// </summary>
/// <remarks>
/// <para>
/// Эталон — <c>groupHairServices</c> из <c>frontend/src/lib/service-groups.ts</c>: группы
/// сортируются по названию специализации (русская локаль), внутри группы сохраняется порядок
/// ответа API, а услуги с неизвестной специализацией не теряются и идут одной последней
/// группой «Прочие услуги».
/// </para>
/// <para>
/// Функция чистая: входные списки и их элементы не меняются, поэтому один и тот же ответ API
/// можно раскладывать повторно — при смене специализации и при возврате к «Любой».
/// </para>
/// </remarks>
public static class SearchServiceGroups
{
    /// <summary>
    /// Сравнение названий специализаций: русская локаль без учёта регистра, как в вебе.
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);

    /// <summary>
    /// Строит группы услуг для сгруппированного списка.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Без <paramref name="specializationId"/> возвращаются все группы справочника; услуги, чьей
    /// специализации нет в <paramref name="specializations"/> (в том числе с
    /// <see cref="Guid.Empty"/>), собираются в одну последнюю группу с заголовком
    /// <paramref name="otherServicesTitle"/> — такие строки нельзя потерять, если специализацию
    /// выключили между двумя запросами.
    /// </para>
    /// <para>
    /// С заданным <paramref name="specializationId"/> в результат попадают только услуги этой
    /// специализации: чужие и «прочие» строки отбрасываются, поэтому хвостовой группы в этом
    /// режиме нет. Если услуг у специализации нет, возвращается пустой список — пустая группа
    /// не нужна.
    /// </para>
    /// </remarks>
    /// <param name="services">Услуги справочника в порядке ответа API.</param>
    /// <param name="specializations">
    /// Специализации справочника: из них берутся заголовки и порядок групп.
    /// </param>
    /// <param name="specializationId">
    /// Специализация, выбранная в фильтре; <c>null</c> — сужения нет.
    /// </param>
    /// <param name="otherServicesTitle">Заголовок группы услуг с неизвестной специализацией.</param>
    /// <returns>Непустые группы: специализации по алфавиту, «прочие» последней.</returns>
    public static IReadOnlyList<SearchHairServiceGroup> Build(
        IReadOnlyList<HairServiceResponse> services,
        IReadOnlyList<SpecializationResponse> specializations,
        Guid? specializationId,
        string otherServicesTitle)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(specializations);
        ArgumentNullException.ThrowIfNull(otherServicesTitle);

        var isFiltered = specializationId is not null;
        var knownIds = new HashSet<Guid>(specializations.Select(row => row.Id));
        var bySpecialization = new Dictionary<Guid, List<SearchCatalogOption>>();
        var unknown = new List<SearchCatalogOption>();

        foreach (var service in services)
        {
            if (isFiltered && service.SpecializationId != specializationId)
            {
                continue;
            }

            var option = SearchCatalogOption.From(service.Id, service.Name, service.SpecializationId);

            if (!knownIds.Contains(service.SpecializationId))
            {
                // В режиме выбранной специализации хвоста нет: её id пришёл из этого же
                // справочника, поэтому «прочих» строк там быть не может.
                if (!isFiltered)
                {
                    unknown.Add(option);
                }

                continue;
            }

            if (!bySpecialization.TryGetValue(service.SpecializationId, out var group))
            {
                group = [];
                bySpecialization.Add(service.SpecializationId, group);
            }

            group.Add(option);
        }

        // Сортируем копию: входной список специализаций принадлежит вызывающему коду.
        var ordered = new List<SpecializationResponse>(specializations);

        ordered.Sort(static (left, right) =>
        {
            var byName = NameComparer.Compare(left.Name, right.Name);

            return byName != 0 ? byName : left.Id.CompareTo(right.Id);
        });

        var groups = new List<SearchHairServiceGroup>(ordered.Count);

        foreach (var specialization in ordered)
        {
            if (bySpecialization.TryGetValue(specialization.Id, out var group))
            {
                groups.Add(new SearchHairServiceGroup(specialization.Name, group));
            }
        }

        if (unknown.Count > 0)
        {
            groups.Add(new SearchHairServiceGroup(otherServicesTitle, unknown));
        }

        return groups;
    }
}
