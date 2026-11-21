namespace BarberSearch.Mobile.Core.Layout;

/// <summary>
/// Раскладка сетки слотов: сколько колонок помещается и какая ширина достаётся каждой ячейке.
/// </summary>
/// <param name="Columns">Число колонок в строке, не меньше одной.</param>
/// <param name="SlotWidth">Ширина ячейки в независимых от плотности единицах.</param>
public readonly record struct SlotGridLayout(int Columns, double SlotWidth);

/// <summary>
/// Считает сетку чипов «Свободное время»: одинаковую ширину ячеек и выравнивание по колонкам.
/// </summary>
/// <remarks>
/// <para>
/// Веб раскладывает чипы содержимым: время приёма там — только начало («18:30»), поэтому
/// ширины совпадают сами. На телефоне чип показывает диапазон «18:30–19:15», и его длина зависит
/// от разрядности часов: «9:00–9:30» короче «18:30–19:15». Свободная укладка превращала это в
/// рваные ряды разной ширины, поэтому приложение считает сетку — все ячейки одной ширины.
/// </para>
/// <para>
/// Колонки выводятся из доступной ширины, а не заданы числом: на узком телефоне две ячейки
/// по 143 pt читаются, на широком помещаются три по 110 pt. Минимум в 110 pt — это чип с
/// диапазоном времени и отступами 12 pt по бокам: уже него текст пришлось бы обрезать.
/// </para>
/// </remarks>
public static class SlotGridMetrics
{
    /// <summary>
    /// Минимальная ширина чипа по умолчанию: диапазон времени и боковые отступы.
    /// </summary>
    public const double DefaultSlotWidth = 110;

    /// <summary>
    /// Предел колонок по умолчанию: на планшете чипы не растягиваются на пол-экрана.
    /// </summary>
    public const int DefaultMaxColumns = 6;

    /// <summary>
    /// Расстояние между ячейками по умолчанию; оно же — вертикальный отступ строк.
    /// </summary>
    public const double DefaultSpacing = 8;

    /// <summary>
    /// Раскладывает сетку по доступной ширине.
    /// </summary>
    /// <param name="width">Доступная ширина контейнера в независимых от плотности единицах.</param>
    /// <param name="minimumSlotWidth">Минимальная ширина чипа; неположительная — один пункт.</param>
    /// <param name="maximumColumns">Предел колонок; меньше единицы — одна колонка.</param>
    /// <param name="spacing">Расстояние между ячейками; отрицательное — без отступов.</param>
    /// <returns>Число колонок и одинаковая для всех ячеек ширина.</returns>
    public static SlotGridLayout Resolve(
        double width,
        double minimumSlotWidth = DefaultSlotWidth,
        int maximumColumns = DefaultMaxColumns,
        double spacing = DefaultSpacing)
    {
        var minimum = double.IsFinite(minimumSlotWidth) && minimumSlotWidth > 0
            ? minimumSlotWidth
            : 1;
        var gap = double.IsFinite(spacing) && spacing > 0 ? spacing : 0;
        var limit = maximumColumns >= 1 ? maximumColumns : 1;

        // Ширины нет (бесконечное измерение или пустой контейнер): одна колонка минимальной
        // ширины — раскладка обязана вернуть что-то осмысленное, а не ноль.
        if (!double.IsFinite(width) || width <= 0)
        {
            return new SlotGridLayout(1, minimum);
        }

        // В строку помещается n ячеек и n − 1 промежутков: width ≥ n·min + (n − 1)·gap.
        var columns = (int)Math.Floor((width + gap) / (minimum + gap));
        columns = Math.Clamp(columns, 1, limit);

        var slotWidth = (width - gap * (columns - 1)) / columns;

        return new SlotGridLayout(columns, slotWidth);
    }
}
