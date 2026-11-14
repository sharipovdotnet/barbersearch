namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Пункт списка «О мастере»: готовая строка с маркером.
/// </summary>
/// <remarks>
/// Маркер свёрстан текстом, а не <c>list-disc</c> веба: у MAUI нет списков с точками,
/// а картинок и иконок в проекте нет. Строка собирается один раз — шаблон её только
/// показывает.
/// </remarks>
public sealed class BarberInfoItem
{
    /// <summary>
    /// Создаёт пункт списка.
    /// </summary>
    /// <param name="text">Текст пункта из профиля.</param>
    public BarberInfoItem(string text)
    {
        Text = string.Concat("• ", text);
    }

    /// <summary>
    /// Готовая строка: маркер и текст.
    /// </summary>
    public string Text { get; }
}

/// <summary>
/// Список раздела «О мастере»: подзаголовок и его пункты.
/// </summary>
/// <remarks>
/// Пять списков профиля показываются одним шаблоном, поэтому заголовок живёт вместе
/// с пунктами: пустой список не создаётся вовсе, и заголовок без данных не рисуется.
/// </remarks>
public sealed class BarberInfoList
{
    private BarberInfoList(string title, IReadOnlyList<BarberInfoItem> items)
    {
        Title = title;
        Items = items;
    }

    /// <summary>
    /// Подзаголовок списка: «Сертификаты и обучение», «Достижения» и т. д.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Пункты списка; у созданного списка их всегда хотя бы один.
    /// </summary>
    public IReadOnlyList<BarberInfoItem> Items { get; }

    /// <summary>
    /// Создаёт список, отбрасывая пустые пункты.
    /// </summary>
    /// <remarks>
    /// Сервер присылает непустые коллекции, но строки в них могут быть пустыми: пустая
    /// строка с маркером — шум, поэтому такой пункт не попадает в список.
    /// </remarks>
    /// <param name="title">Подзаголовок.</param>
    /// <param name="items">Строки из профиля.</param>
    /// <returns>Список либо <c>null</c>, если показывать нечего.</returns>
    public static BarberInfoList? Create(string title, IReadOnlyList<string>? items)
    {
        if (items is null)
        {
            return null;
        }

        var lines = items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new BarberInfoItem(item.Trim()))
            .ToArray();

        return lines.Length == 0 ? null : new BarberInfoList(title, lines);
    }
}
