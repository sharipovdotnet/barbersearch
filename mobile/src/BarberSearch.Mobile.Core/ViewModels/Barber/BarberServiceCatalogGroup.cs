namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Группа услуг справочника одной специализации в редакторе прайса.
/// </summary>
/// <remarks>
/// <para>
/// Плоский справочник в десятки услуг мастеру приходилось читать целиком: строки шли в порядке
/// API, и понять, к какому направлению относится «Тейпер», можно было только по бейджу категории.
/// Группа ставит специализацию заголовком — так же, как прайс в разделе «Профиль» и фильтр
/// «Услуга» на «Поиске», — и строки под ним читаются перечислением этого направления.
/// </para>
/// <para>
/// Группа — неизменяемый снимок готовых строк: её собирает чистая функция
/// <see cref="BarberServiceCatalogGroups.Build"/>, а не сам справочник.
/// </para>
/// </remarks>
public sealed class BarberServiceCatalogGroup
{
    /// <summary>
    /// Создаёт группу услуг с заголовком.
    /// </summary>
    /// <param name="title">Заголовок группы: название специализации или «Прочие услуги».</param>
    /// <param name="services">Услуги специализации в порядке ответа API.</param>
    public BarberServiceCatalogGroup(string title, IReadOnlyList<BarberServiceCatalogItem> services)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(services);

        Title = title;
        Services = services;
    }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Услуги этой специализации в порядке ответа API.
    /// </summary>
    public IReadOnlyList<BarberServiceCatalogItem> Services { get; }
}
