using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Пункт фильтра «Дата»: календарный день салона или «Любая дата».
/// </summary>
/// <remarks>
/// Веб отдаёт нативный календарь, из которого дату можно и не выбирать; на телефоне такой же
/// выбор — это список, поэтому первым пунктом стоит «Любая дата», а подписи ближайших дней
/// читаются как «Сегодня, 15 ноября» и «Завтра, 16 ноября».
/// </remarks>
public sealed class SearchDateOption
{
    private SearchDateOption(DateOnly? date, string displayText)
    {
        Date = date;
        DisplayText = displayText;
    }

    /// <summary>
    /// Календарная дата для запроса; <c>null</c> — «фильтра нет».
    /// </summary>
    public DateOnly? Date { get; }

    /// <summary>
    /// Подпись в <c>Picker</c>: «Любая дата», «Сегодня, …», «Завтра, …» или «пн, 17 ноября».
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Пункт «фильтра нет» с заданной подписью.
    /// </summary>
    /// <param name="text">Подпись пункта.</param>
    /// <returns>Пункт без даты.</returns>
    public static SearchDateOption Any(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SearchDateOption(null, text);
    }

    /// <summary>
    /// Пункт с датой; сегодня и завтра подписаны словами, остальные дни — с днём недели.
    /// </summary>
    /// <param name="date">Календарная дата фильтра.</param>
    /// <param name="today">Сегодняшняя дата устройства — точка отсчёта подписи.</param>
    /// <returns>Пункт для <c>Picker</c>.</returns>
    public static SearchDateOption For(DateOnly date, DateOnly today)
    {
        if (date == today)
        {
            return new SearchDateOption(date, string.Concat("Сегодня, ", SalonClock.FormatDayMonth(date)));
        }

        if (date == today.AddDays(1))
        {
            return new SearchDateOption(date, string.Concat("Завтра, ", SalonClock.FormatDayMonth(date)));
        }

        return new SearchDateOption(date, SalonClock.FormatWeekdayDate(date));
    }
}
