using BarberSearch.Mobile.Core.Layout;
using Microsoft.Maui.Layouts;

namespace BarberSearch.Mobile.Controls;

/// <summary>
/// Сетка работ «Работы»: квадратные плитки одной ширины, выровненные по колонкам.
/// </summary>
/// <remarks>
/// <para>
/// Штатный <see cref="FlexLayout"/> раскладывает плитки по их содержимому и базису: с
/// <c>Basis="48%"</c> и отступом 8 pt на каждой плитке два столбца не помещались в строку
/// (2 × 142,08 + 16 = 300,2 pt при 296 pt внутри карточки), и плитки переносились по одной,
/// оставляя пустую половину справа. Фиксированная высота 160 pt при разной ширине давала
/// плиткам разное соотношение сторон, поэтому квадраты тоже не совпадали.
/// </para>
/// <para>
/// Здесь число колонок выводится из доступной ширины (<see cref="PhotoGridMetrics"/>), и каждая
/// ячейка получает ровно одну и ту же сторону: ширина задаёт высоту. <see cref="ColumnSpacing"/>
/// и <see cref="RowSpacing"/> заменяют отступы, которые раньше висели на каждой плитке; 8 pt по
/// вертикали сохраняются намеренно — этот размер уже нравится человеку. Веб задаёт колонки
/// контрольными точками CSS (2/3/4), на телефоне их заменяет предел
/// <see cref="MaximumColumns"/>: две колонки на телефоне и до четырёх на широком экране.
/// </para>
/// <para>
/// Ячейка измеряется как <c>tile × tile</c>, чтобы картинка получила ограничение, но результат
/// измерения ребёнка не меняет размер ячейки: <see cref="Border"/> с <see cref="Image"/> внутри
/// может измериться в ноль, и сетка не должна от этого схлопнуться.
/// </para>
/// </remarks>
public sealed class PhotoGrid : Layout
{
    /// <summary>
    /// Биндинг-свойство <see cref="MinimumTileWidth"/>.
    /// </summary>
    public static readonly BindableProperty MinimumTileWidthProperty = BindableProperty.Create(
        nameof(MinimumTileWidth),
        typeof(double),
        typeof(PhotoGrid),
        PhotoGridMetrics.DefaultTileWidth,
        validateValue: (_, value) => value is double width && width > 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="MaximumColumns"/>.
    /// </summary>
    public static readonly BindableProperty MaximumColumnsProperty = BindableProperty.Create(
        nameof(MaximumColumns),
        typeof(int),
        typeof(PhotoGrid),
        PhotoGridMetrics.DefaultMaxColumns,
        validateValue: (_, value) => value is int columns && columns >= 1,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="ColumnSpacing"/>.
    /// </summary>
    public static readonly BindableProperty ColumnSpacingProperty = BindableProperty.Create(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(PhotoGrid),
        PhotoGridMetrics.DefaultSpacing,
        validateValue: (_, value) => value is double spacing && spacing >= 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="RowSpacing"/>.
    /// </summary>
    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(
        nameof(RowSpacing),
        typeof(double),
        typeof(PhotoGrid),
        PhotoGridMetrics.DefaultSpacing,
        validateValue: (_, value) => value is double spacing && spacing >= 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Наименьшая сторона плитки: уже неё квадрат перестаёт читаться как работа.
    /// </summary>
    public double MinimumTileWidth
    {
        get => (double)GetValue(MinimumTileWidthProperty);
        set => SetValue(MinimumTileWidthProperty, value);
    }

    /// <summary>
    /// Предел числа колонок: на широком экране плитки не растягиваются на пол-экрана.
    /// </summary>
    public int MaximumColumns
    {
        get => (int)GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    /// <summary>
    /// Расстояние между плитками по горизонтали.
    /// </summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>
    /// Расстояние между строками по вертикали.
    /// </summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <inheritdoc />
    protected override ILayoutManager CreateLayoutManager() => new PhotoGridLayoutManager(this);

    /// <summary>
    /// Считает колонки и сторону плитки для доступной ширины.
    /// </summary>
    /// <param name="width">Доступная ширина контейнера.</param>
    /// <returns>Раскладка с одинаковой стороной всех квадратных ячеек.</returns>
    internal PhotoGridLayout Resolve(double width) =>
        PhotoGridMetrics.Resolve(width, MinimumTileWidth, MaximumColumns, ColumnSpacing);

    private static void OnMetricsChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((PhotoGrid)bindable).InvalidateMeasure();

    /// <summary>
    /// Менеджер раскладки: измеряет плитки одной стороной и раскладывает их по общей сетке.
    /// </summary>
    private sealed class PhotoGridLayoutManager : ILayoutManager
    {
        private readonly PhotoGrid _grid;

        /// <summary>
        /// Создаёт менеджер для сетки.
        /// </summary>
        /// <param name="grid">Раскладываемая сетка.</param>
        public PhotoGridLayoutManager(PhotoGrid grid) => _grid = grid;

        /// <inheritdoc />
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var width = double.IsFinite(widthConstraint) && widthConstraint > 0
                ? widthConstraint
                : _grid.MinimumTileWidth;
            var layout = _grid.Resolve(width);

            var visible = 0;

            for (var index = 0; index < _grid.Count; index++)
            {
                var child = _grid[index];
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                // Размер ребёнка не участвует в расчёте ячейки: квадрат задаёт ширина.
                child.Measure(layout.TileSize, layout.TileSize);
                visible++;
            }

            if (visible == 0)
            {
                return new Size(width, 0);
            }

            var rows = (visible + layout.Columns - 1) / layout.Columns;
            var height = rows * layout.TileSize + (rows - 1) * _grid.RowSpacing;

            return new Size(width, height);
        }

        /// <inheritdoc />
        public Size ArrangeChildren(Rect bounds)
        {
            var tiles = new List<IView>(_grid.Count);

            for (var index = 0; index < _grid.Count; index++)
            {
                var child = _grid[index];
                if (child.Visibility != Visibility.Collapsed)
                {
                    tiles.Add(child);
                }
            }

            if (tiles.Count == 0)
            {
                return bounds.Size;
            }

            var width = bounds.Width > 0 ? bounds.Width : _grid.MinimumTileWidth;
            var layout = _grid.Resolve(width);

            for (var index = 0; index < tiles.Count; index++)
            {
                var column = index % layout.Columns;
                var row = index / layout.Columns;
                var x = bounds.Left + column * (layout.TileSize + _grid.ColumnSpacing);
                var y = bounds.Top + row * (layout.TileSize + _grid.RowSpacing);

                tiles[index].Arrange(new Rect(x, y, layout.TileSize, layout.TileSize));
            }

            return bounds.Size;
        }
    }
}
