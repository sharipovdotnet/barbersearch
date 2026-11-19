using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Раздел администрирования в списке телефона: заголовок, состояние очереди и бейдж.
/// </summary>
/// <remarks>
/// <para>
/// Это замена горизонтальному рейлу веба (<c>admin-rail.tsx</c>): на телефоне десять пунктов
/// не помещаются в одну ленту, а те, что помещаются, не читаются. Строка несёт ровно то, что
/// рейл объяснял подсказкой и бейджем, — число ожидающих и смысл очереди, — поэтому число
/// видно без наведения, которого на телефоне нет.
/// </para>
/// <para>
/// Строка живёт всё время жизни экрана: сводка перечитывается, а коллекция не пересобирается,
/// иначе прокрутка сбрасывалась бы после каждого обновления.
/// </para>
/// </remarks>
public sealed partial class AdminSectionItem : ObservableObject
{
    /// <summary>
    /// Создаёт строку раздела.
    /// </summary>
    /// <param name="route">Маршрут раздела в Shell.</param>
    /// <param name="title">Название раздела.</param>
    public AdminSectionItem(string route, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Route = route;
        Title = title;
    }

    /// <summary>
    /// Маршрут раздела в Shell.
    /// </summary>
    public string Route { get; }

    /// <summary>
    /// Название раздела.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Сколько ждёт решения.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private int _pendingCount;

    /// <summary>
    /// Состояние очереди словами: число с пояснением либо «проверено».
    /// </summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>
    /// Вторая строка о самом срочном в очереди; <c>null</c>, если срочного нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _detail;

    /// <summary>
    /// Есть ли что ждёт решения: при нуле бейджа нет.
    /// </summary>
    public bool HasBadge => PendingCount > 0;

    /// <summary>
    /// Подпись бейджа: до 99 цифрами, дальше «99+».
    /// </summary>
    public string BadgeText => AdminText.BadgeCount(PendingCount);

    /// <summary>
    /// Есть ли вторая строка.
    /// </summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>
    /// Заполняет строку снимком очереди.
    /// </summary>
    /// <param name="pendingCount">Сколько ждёт решения.</param>
    /// <param name="subtitle">Состояние очереди словами.</param>
    /// <param name="detail">Самое срочное в очереди либо <c>null</c>.</param>
    public void Apply(int pendingCount, string subtitle, string? detail)
    {
        ArgumentNullException.ThrowIfNull(subtitle);

        PendingCount = pendingCount;
        Subtitle = subtitle;
        Detail = detail;
    }
}
