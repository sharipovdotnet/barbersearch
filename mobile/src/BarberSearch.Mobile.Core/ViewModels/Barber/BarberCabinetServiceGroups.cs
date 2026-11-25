namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раскладывает прайс кабинета по специализациям — как фильтр «Услуга» на «Поиске».
/// </summary>
/// <remarks>
/// <para>
/// Группы идут в порядке первого появления специализации в <c>services</c>, а строки внутри
/// группы сохраняют порядок ответа API: кабинет не пересортировывает свой прайс, поэтому
/// группа — это только заголовок над уже существующим порядком. Функция чистая: ни входной
/// список, ни его элементы не меняются.
/// </para>
/// <para>
/// Скрытые услуги остаются на месте: владелец видит прайс целиком, и <see cref="BarberServiceItem.IsHidden"/>
/// по-прежнему рисует бейдж «Скрыта» на строке. Строки без специализации (пустая строка или
/// одни пробелы) уходят в одну последнюю группу с заголовком <c>otherServicesTitle</c> —
/// «Прочие услуги». Название специализации сравнивается после обрезки пробелов по краям:
/// идентификатора специализации в строке прайса нет, а API отдаёт имя из общего справочника.
/// </para>
/// </remarks>
public static class BarberCabinetServiceGroups
{
    /// <summary>
    /// Строит группы услуг для списка под заголовками.
    /// </summary>
    /// <param name="services">Прайс в порядке ответа API.</param>
    /// <param name="otherServicesTitle">Заголовок группы услуг без специализации.</param>
    /// <returns>
    /// Непустые группы в порядке первого появления специализации; «прочие» — последней.
    /// </returns>
    public static IReadOnlyList<BarberCabinetServiceGroup> Build(
        IReadOnlyList<BarberServiceItem> services,
        string otherServicesTitle)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(otherServicesTitle);

        var order = new List<(string Title, List<BarberServiceItem> Services)>();
        var byTitle = new Dictionary<string, List<BarberServiceItem>>(StringComparer.Ordinal);
        var unknown = new List<BarberServiceItem>();

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

        var groups = new List<BarberCabinetServiceGroup>(order.Count + (unknown.Count > 0 ? 1 : 0));

        foreach (var (title, group) in order)
        {
            groups.Add(new BarberCabinetServiceGroup(title, group));
        }

        if (unknown.Count > 0)
        {
            groups.Add(new BarberCabinetServiceGroup(otherServicesTitle, unknown));
        }

        return groups;
    }
}
