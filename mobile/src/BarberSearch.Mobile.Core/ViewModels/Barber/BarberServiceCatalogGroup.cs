using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

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
/// Заголовок группы — кнопка сворачивания: открытая группа показывает строки, закрытая прячет их
/// и оставляет на экране только название направления, счётчик и шеврон. Девять услуг четырёх
/// направлений под рукой и не мешают друг другу; раскрывается не аккордеоном, а по одной группе,
/// потому что мастер сравнивает услуги двух направлений и закрывать только что открытое ему не
/// помогают.
/// </para>
/// <para>
/// <see cref="Services"/> — полный снимок строк группы в порядке ответа API, его читают счётчик и
/// восстановление раскрытия. <see cref="VisibleServices"/> — то, что видно сейчас: при пустом
/// поиске тот же снимок, при запросе — только совпадения. Строки не пересоздаются: подсветка
/// выбора и длительность живут на самом экземпляре <see cref="BarberServiceCatalogItem"/>.
/// </para>
/// </remarks>
public sealed partial class BarberServiceCatalogGroup : ObservableObject
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
        _visibleServices = services;
        _matchCount = services.Count;
    }

    /// <summary>
    /// Заголовок группы в списке.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Услуги этой специализации в порядке ответа API.
    /// </summary>
    public IReadOnlyList<BarberServiceCatalogItem> Services { get; }

    /// <summary>
    /// Группа раскрыта: строки видны под заголовком.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Строки, видимые сейчас: все услуги группы или только совпадения с поиском.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<BarberServiceCatalogItem> _visibleServices;

    /// <summary>
    /// Сколько услуг группы проходит текущий поиск; при пустом поиске — все.
    /// </summary>
    /// <remarks>
    /// Счётчик в заголовке считает видимое, а не весь снимок: после запроса «стрижка» у группы
    /// должно стоять число её совпадений, иначе счётчик обещал бы строки, которых под заголовком
    /// нет.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _matchCount;

    /// <summary>
    /// В группе есть строки для показа: пустая группа не рисуется вовсе.
    /// </summary>
    public bool HasMatches => MatchCount > 0;

    /// <summary>
    /// Счётчик видимых услуг для заголовка: одни цифры, без слова «услуг».
    /// </summary>
    /// <remarks>
    /// Ключа множественного числа для «услуг» в локализации нет, а счётчик — подсказка, а не
    /// фраза: «3» рядом с заголовком читается сам.
    /// </remarks>
    public string CountText => MatchCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Оставляет в группе строки, подходящие под запрос.
    /// </summary>
    /// <param name="query">Строка поиска; пустая возвращает все строки группы.</param>
    /// <remarks>
    /// Список строк пересобирается, а не фильтруется на месте: <see cref="Services"/> — снимок
    /// API-порядка, и после очистки поиска группа обязана показать его целиком, в том же порядке.
    /// Сами строки остаются теми же экземплярами.
    /// </remarks>
    public void ApplyQuery(string? query)
    {
        if (BarberServiceCatalogFilter.IsEmpty(query))
        {
            VisibleServices = Services;
            MatchCount = Services.Count;

            return;
        }

        var matches = new List<BarberServiceCatalogItem>(Services.Count);

        foreach (var service in Services)
        {
            if (BarberServiceCatalogFilter.Matches(service.Name, query))
            {
                matches.Add(service);
            }
        }

        VisibleServices = matches;
        MatchCount = matches.Count;
    }
}
