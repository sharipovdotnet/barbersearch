namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Раскладывает плоский прайс карточки по специализациям — как фильтр «Услуга» на «Поиске».
/// </summary>
/// <remarks>
/// <para>
/// Группы идут в порядке первого появления специализации в <c>services</c>, а строки внутри
/// группы сохраняют исходный порядок. Публичный прайс уже отсортирован
/// <see cref="BarberServicesOrder"/> (категория → специализация → название), поэтому результат
/// читается сверху вниз как категория и специализация, а не как список бейджей. Функция только
/// собирает ссылки на готовые строки: ни входной список, ни его элементы не меняются.
/// </para>
/// <para>
/// Строки без специализации (пустая строка или одни пробелы) не теряются: они уходят в одну
/// последнюю группу с заголовком <c>otherServicesTitle</c> — «Прочие услуги». Название
/// специализации сравнивается после обрезки пробелов по краям: идентификатора специализации
/// в строке прайса нет, а API отдаёт имя из общего справочника.
/// </para>
/// </remarks>
public static class BarberServiceGroups
{
    /// <summary>
    /// Строит группы услуг для списка под заголовками.
    /// </summary>
    /// <param name="services">Прайс в порядке показа.</param>
    /// <param name="otherServicesTitle">Заголовок группы услуг без специализации.</param>
    /// <returns>
    /// Непустые группы в порядке первого появления специализации; «прочие» — последней.
    /// </returns>
    public static IReadOnlyList<BarberServiceGroup> Build(
        IReadOnlyList<BarberServiceOption> services,
        string otherServicesTitle)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(otherServicesTitle);

        var order = new List<(string Title, List<BarberServiceOption> Services)>();
        var byTitle = new Dictionary<string, List<BarberServiceOption>>(StringComparer.Ordinal);
        var unknown = new List<BarberServiceOption>();

        foreach (var service in services)
        {
            var title = service.SpecializationName.Trim();

            if (title.Length == 0)
            {
                unknown.Add(service);
            }
            else if (byTitle.TryGetValue(title, out var group))
            {
                group.Add(service);
            }
            else
            {
                group = [service];
                byTitle.Add(title, group);
                order.Add((title, group));
            }
        }

        var groups = new List<BarberServiceGroup>(order.Count + (unknown.Count > 0 ? 1 : 0));

        foreach (var (title, group) in order)
        {
            groups.Add(new BarberServiceGroup(title, group));
        }

        if (unknown.Count > 0)
        {
            groups.Add(new BarberServiceGroup(otherServicesTitle, unknown));
        }

        return groups;
    }
}
