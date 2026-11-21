namespace BarberSearch.Mobile.Core.Layout;

/// <summary>
/// Раскладка сетки фотографий: сколько колонок помещается и сторона квадратной плитки.
/// </summary>
/// <param name="Columns">Число колонок в строке, не меньше одной.</param>
/// <param name="TileSize">Сторона плитки в независимых от плотности единицах; высота равна ширине.</param>
public readonly record struct PhotoGridLayout(int Columns, double TileSize);

/// <summary>
/// Считает сетку работ «Работы»: одинаковые квадратные плитки, выровненные по колонкам.
/// </summary>
/// <remarks>
/// <para>
/// Веб раскладывает работы CSS-сеткой <c>grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4</c>,
/// где каждая плитка — <c>aspect-square</c> с <c>object-cover</c> и радиусом 8 pt. На телефоне
/// ту же роль играет <c>PhotoGrid</c>: число колонок выводится из доступной ширины, поэтому
/// контрольные точки веба (640 и 1024 px) заменяются пределом колонок, а не копируются.
/// </para>
/// <para>
/// Арифметика не дублируется: ширина плитки — это ширина ячейки из <see cref="SlotGridMetrics"/>,
/// а высота равна ширине. Минимум в 120 pt — это два столбца на телефоне в 320 pt: внутри
/// карточки остаётся 256 pt, и после промежутка 8 pt каждая плитка получает 124 pt. Предел в
/// четыре колонки — самая широкая сетка веба (<c>lg:grid-cols-4</c>): на планшете плитки дальше
/// растянулись бы на пол-экрана.
/// </para>
/// </remarks>
public static class PhotoGridMetrics
{
    /// <summary>
    /// Минимальная сторона плитки по умолчанию: два столбца ещё помещаются на телефоне в 320 pt.
    /// </summary>
    public const double DefaultTileWidth = 120;

    /// <summary>
    /// Предел колонок по умолчанию: самая широкая сетка веба — четыре колонки.
    /// </summary>
    public const int DefaultMaxColumns = 4;

    /// <summary>
    /// Промежуток между плитками по умолчанию; он же — вертикальный отступ строк.
    /// </summary>
    public const double DefaultSpacing = 8;

    /// <summary>
    /// Раскладывает сетку по доступной ширине.
    /// </summary>
    /// <param name="width">Доступная ширина контейнера в независимых от плотности единицах.</param>
    /// <param name="minimumTileWidth">Минимальная сторона плитки; неположительная — один пункт.</param>
    /// <param name="maximumColumns">Предел колонок; меньше единицы — одна колонка.</param>
    /// <param name="spacing">Промежуток между плитками; отрицательный — без отступов.</param>
    /// <returns>Число колонок и сторона квадратной плитки, одинаковая для всех ячеек.</returns>
    public static PhotoGridLayout Resolve(
        double width,
        double minimumTileWidth = DefaultTileWidth,
        int maximumColumns = DefaultMaxColumns,
        double spacing = DefaultSpacing)
    {
        var layout = SlotGridMetrics.Resolve(width, minimumTileWidth, maximumColumns, spacing);

        return new PhotoGridLayout(layout.Columns, layout.SlotWidth);
    }
}
