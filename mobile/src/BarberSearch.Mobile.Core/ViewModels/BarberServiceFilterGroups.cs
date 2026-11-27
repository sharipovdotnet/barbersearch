using System.Globalization;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Раскладывает пункты фильтра «Услуга» в карточке мастера по специализациям — так же, как это
/// делает веб.
/// </summary>
/// <remarks>
/// <para>
/// Эталон — <c>groupBarberServices</c> из <c>frontend/src/lib/barber-service-groups.ts</c>
/// (PR #206): группы сортируются по названию специализации (русская локаль, без учёта регистра),
/// при совпадении названий — по <c>specializationId</c>, а внутри группы сохраняется порядок
/// ответа API. Устроено как <see cref="SearchServiceGroups.Build"/>: там группы тоже собираются
/// по идентификатору специализации и сортируются русской локалью.
/// </para>
/// <para>
/// «Любая услуга» (<see cref="BarberServiceOption.HairServiceId"/> равно <c>null</c>) — не
/// специализация и не строка группы: она стоит над списком, поэтому в группы не попадает.
/// Услуги без специализации (пустое имя или одни пробелы) не теряются: они уходят одной
/// последней группой с заголовком <c>otherServicesTitle</c> — «Прочие услуги».
/// </para>
/// <para>
/// Активность здесь не проверяется: отбор <c>IsActive</c> уже сделал <c>ApplyProfile</c>, и
/// helper только раскладывает готовые строки. Функция чистая: входной список и его элементы не
/// меняются, группа хранит ссылки на исходные пункты.
/// </para>
/// </remarks>
public static class BarberServiceFilterGroups
{
    /// <summary>
    /// Сравнение названий специализаций: русская локаль без учёта регистра, как в вебе.
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);

    /// <summary>
    /// Строит группы услуг для сгруппированного списка.
    /// </summary>
    /// <param name="services">Пункты выбора в порядке ответа API, включая «Любую услугу».</param>
    /// <param name="otherServicesTitle">Заголовок группы услуг без специализации.</param>
    /// <returns>
    /// Непустые группы: специализации по алфавиту, «прочие» последней. Пустой список услуг даёт
    /// пустой результат.
    /// </returns>
    public static IReadOnlyList<BarberServiceFilterGroup> Build(
        IReadOnlyList<BarberServiceOption> services,
        string otherServicesTitle)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(otherServicesTitle);

        var order = new List<(string Title, Guid SpecializationId, List<BarberServiceOption> Services)>();
        var bySpecialization = new Dictionary<(Guid Id, string Title), List<BarberServiceOption>>();
        var unknown = new List<BarberServiceOption>();

        foreach (var service in services)
        {
            // «Любая услуга» — отсутствие выбора: на карточке она стоит над группами.
            if (service.HairServiceId is null)
            {
                continue;
            }

            var title = service.SpecializationName.Trim();

            if (title.Length == 0)
            {
                unknown.Add(service);
                continue;
            }

            // Ключ — пара «идентификатор + название»: одноимённые, но разные специализации
            // остаются разными группами, а разные названия одного идентификатора не склеиваются.
            var key = (service.SpecializationId, title);

            if (!bySpecialization.TryGetValue(key, out var group))
            {
                group = [];
                bySpecialization.Add(key, group);
                order.Add((title, service.SpecializationId, group));
            }

            group.Add(service);
        }

        // Сортируем собственный список групп, а не вход: вход принадлежит вызывающему коду.
        order.Sort(static (left, right) =>
        {
            var byName = NameComparer.Compare(left.Title, right.Title);

            return byName != 0 ? byName : left.SpecializationId.CompareTo(right.SpecializationId);
        });

        var groups = new List<BarberServiceFilterGroup>(
            order.Count + (unknown.Count > 0 ? 1 : 0));

        foreach (var (title, _, group) in order)
        {
            groups.Add(new BarberServiceFilterGroup(title, group));
        }

        if (unknown.Count > 0)
        {
            groups.Add(new BarberServiceFilterGroup(otherServicesTitle, unknown));
        }

        return groups;
    }
}
