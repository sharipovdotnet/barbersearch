using System.Globalization;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раскладывает справочник услуг по специализациям для списка «Добавить услугу».
/// </summary>
/// <remarks>
/// <para>
/// Эталон — веб (<c>groupHairServices</c> из <c>frontend/src/lib/service-groups.ts</c>): услуги
/// идут группами по специализациям, заголовок группы — название специализации, а услуга без
/// известной специализации не теряется и попадает в одну последнюю группу «Прочие услуги».
/// </para>
/// <para>
/// Порядок групп — единственное отличие от веба, и оно сделано для мастера: специализации
/// сортируются по категории в каталожном порядке (<see cref="SearchSpecializationGroups.CategoryOrder"/>:
/// стрижка, борода, окрашивание, укладка, уход, другое), а внутри категории — по названию в
/// русской локали. Алфавит по всем группам, как в вебе, перемешал бы направления: «Борода»
/// попала бы между «Блонд-мастером» и «Британским фейдом», и мастер, который добавляет услугу
/// своего направления, искал бы его по всему списку. Каталожный порядок — тот же, в котором
/// специализации перечислены в фильтре «Специализация», поэтому список читается сверху вниз
/// так же, как каталог. Внутри группы сохраняется порядок ответа API (сервер отдаёт по имени),
/// и он же — порядок, в котором услуги стоят в фильтре «Услуга».
/// </para>
/// <para>
/// Функция чистая: входные списки и их элементы не меняются, поэтому повторное чтение
/// справочника раскладывается заново без побочных эффектов. Равные названия специализаций
/// разводятся идентификатором, чтобы порядок не зависел от порядка ответа.
/// </para>
/// </remarks>
public static class BarberServiceCatalogGroups
{
    /// <summary>
    /// Сравнение названий специализаций: русская локаль без учёта регистра, как в вебе.
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);

    /// <summary>
    /// Строит группы услуг под заголовками специализаций.
    /// </summary>
    /// <param name="services">Услуги справочника, уже без тех, что стоят в прайсе.</param>
    /// <param name="specializations">Специализации справочника: из них берутся заголовки и порядок.</param>
    /// <param name="otherServicesTitle">Заголовок группы услуг с неизвестной специализацией.</param>
    /// <returns>Непустые группы: по категориям и названию, «прочие» последней.</returns>
    public static IReadOnlyList<BarberServiceCatalogGroup> Build(
        IReadOnlyList<BarberServiceCatalogItem> services,
        IReadOnlyList<SpecializationResponse> specializations,
        string otherServicesTitle)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(specializations);
        ArgumentNullException.ThrowIfNull(otherServicesTitle);

        var known = new Dictionary<Guid, SpecializationResponse>(specializations.Count);

        foreach (var specialization in specializations)
        {
            known.TryAdd(specialization.Id, specialization);
        }

        var bySpecialization = new Dictionary<Guid, List<BarberServiceCatalogItem>>();
        var unknown = new List<BarberServiceCatalogItem>();

        foreach (var service in services)
        {
            if (!known.ContainsKey(service.SpecializationId))
            {
                // Специализацию выключили между двумя запросами или услуга ссылается на неё
                // пустым идентификатором: строку нельзя терять, её ищут по названию.
                unknown.Add(service);

                continue;
            }

            if (!bySpecialization.TryGetValue(service.SpecializationId, out var group))
            {
                group = [];
                bySpecialization.Add(service.SpecializationId, group);
            }

            group.Add(service);
        }

        // Сортируем копию: входной список специализаций принадлежит вызывающему коду.
        var ordered = new List<SpecializationResponse>(specializations);

        ordered.Sort(static (left, right) =>
        {
            var byCategory = CategoryRank(left.Category).CompareTo(CategoryRank(right.Category));

            if (byCategory != 0)
            {
                return byCategory;
            }

            var byName = NameComparer.Compare(left.Name, right.Name);

            return byName != 0 ? byName : left.Id.CompareTo(right.Id);
        });

        var groups = new List<BarberServiceCatalogGroup>(bySpecialization.Count + 1);

        foreach (var specialization in ordered)
        {
            if (bySpecialization.TryGetValue(specialization.Id, out var group))
            {
                groups.Add(new BarberServiceCatalogGroup(specialization.Name, group));
            }
        }

        if (unknown.Count > 0)
        {
            groups.Add(new BarberServiceCatalogGroup(otherServicesTitle, unknown));
        }

        return groups;
    }

    /// <summary>
    /// Место категории в каталожном порядке; неизвестная категория — в конце.
    /// </summary>
    /// <param name="category">Категория специализации.</param>
    /// <returns>Индекс в <see cref="SearchSpecializationGroups.CategoryOrder"/>.</returns>
    private static int CategoryRank(ServiceCategory category)
    {
        var order = SearchSpecializationGroups.CategoryOrder;

        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == category)
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
