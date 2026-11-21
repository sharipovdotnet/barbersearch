using BarberSearch.Mobile.Core.Layout;
using Microsoft.Maui.Layouts;

namespace BarberSearch.Mobile.Controls;

/// <summary>
/// Сетка чипов «Свободное время»: ячейки одной ширины и высоты, выровненные по колонкам.
/// </summary>
/// <remarks>
/// <para>
/// Штатный <see cref="FlexLayout"/> укладывает чипы по их содержимому. Время приёма — диапазон
/// «18:30–19:15», и его длина зависит от разрядности часов, поэтому чипы получались разной
/// ширины, а строки заканчивались на разной высоте. Здесь число колонок выводится из доступной
/// ширины (<see cref="SlotGridMetrics"/>), и каждая ячейка получает ровно одну и ту же ширину.
/// </para>
/// <para>
/// Высота строки — самая высокая ячейка строки, а все строки растягиваются до одного размера;
/// <see cref="RowSpacing"/> и <see cref="ColumnSpacing"/> заменяют отступы, которые раньше
/// висели на каждом чипе. Минимальный тап-таргет 44 pt приходит из стиля <c>Button</c>.
/// </para>
/// </remarks>
public sealed class SlotGrid : Layout
{
    /// <summary>
    /// Биндинг-свойство <see cref="MinimumSlotWidth"/>.
    /// </summary>
    public static readonly BindableProperty MinimumSlotWidthProperty = BindableProperty.Create(
        nameof(MinimumSlotWidth),
        typeof(double),
        typeof(SlotGrid),
        SlotGridMetrics.DefaultSlotWidth,
        validateValue: (_, value) => value is double width && width > 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="MaximumColumns"/>.
    /// </summary>
    public static readonly BindableProperty MaximumColumnsProperty = BindableProperty.Create(
        nameof(MaximumColumns),
        typeof(int),
        typeof(SlotGrid),
        SlotGridMetrics.DefaultMaxColumns,
        validateValue: (_, value) => value is int columns && columns >= 1,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="ColumnSpacing"/>.
    /// </summary>
    public static readonly BindableProperty ColumnSpacingProperty = BindableProperty.Create(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(SlotGrid),
        SlotGridMetrics.DefaultSpacing,
        validateValue: (_, value) => value is double spacing && spacing >= 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Биндинг-свойство <see cref="RowSpacing"/>.
    /// </summary>
    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(
        nameof(RowSpacing),
        typeof(double),
        typeof(SlotGrid),
        SlotGridMetrics.DefaultSpacing,
        validateValue: (_, value) => value is double spacing && spacing >= 0,
        propertyChanged: OnMetricsChanged);

    /// <summary>
    /// Наименьшая ширина чипа: уже неё диапазон времени пришлось бы обрезать.
    /// </summary>
    public double MinimumSlotWidth
    {
        get => (double)GetValue(MinimumSlotWidthProperty);
        set => SetValue(MinimumSlotWidthProperty, value);
    }

    /// <summary>
    /// Предел числа колонок: на широком экране чипы не растягиваются на пол-экрана.
    /// </summary>
    public int MaximumColumns
    {
        get => (int)GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    /// <summary>
    /// Расстояние между ячейками по горизонтали.
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
    protected override ILayoutManager CreateLayoutManager() => new SlotGridLayoutManager(this);

    /// <summary>
    /// Считает колонки и ширину ячейки для доступной ширины.
    /// </summary>
    /// <param name="width">Доступная ширина контейнера.</param>
    /// <returns>Раскладка с одинаковой шириной всех ячеек.</returns>
    internal SlotGridLayout Resolve(double width) =>
        SlotGridMetrics.Resolve(width, MinimumSlotWidth, MaximumColumns, ColumnSpacing);

    private static void OnMetricsChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SlotGrid)bindable).InvalidateMeasure();

    /// <summary>
    /// Менеджер раскладки: измеряет чипы одной шириной и раскладывает их по общей сетке.
    /// </summary>
    private sealed class SlotGridLayoutManager : ILayoutManager
    {
        /// <summary>Высота чипа до первого измерения — минимальный тап-таргет из стиля.</summary>
        private const double FallbackSlotHeight = 44;

        private readonly SlotGrid _grid;

        /// <summary>
        /// Создаёт менеджер для сетки.
        /// </summary>
        /// <param name="grid">Раскладываемая сетка.</param>
        public SlotGridLayoutManager(SlotGrid grid) => _grid = grid;

        /// <inheritdoc />
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var width = double.IsFinite(widthConstraint) && widthConstraint > 0
                ? widthConstraint
                : _grid.MinimumSlotWidth;
            var layout = _grid.Resolve(width);

            var rowHeight = 0d;
            var visible = 0;

            for (var index = 0; index < _grid.Count; index++)
            {
                var child = _grid[index];
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var measured = child.Measure(layout.SlotWidth, heightConstraint);
                rowHeight = Math.Max(rowHeight, measured.Height);
                visible++;
            }

            if (visible == 0)
            {
                return new Size(width, 0);
            }

            if (rowHeight <= 0)
            {
                rowHeight = FallbackSlotHeight;
            }

            var rows = (visible + layout.Columns - 1) / layout.Columns;
            var height = rows * rowHeight + (rows - 1) * _grid.RowSpacing;

            return new Size(width, height);
        }

        /// <inheritdoc />
        public Size ArrangeChildren(Rect bounds)
        {
            var slots = new List<IView>(_grid.Count);

            for (var index = 0; index < _grid.Count; index++)
            {
                var child = _grid[index];
                if (child.Visibility != Visibility.Collapsed)
                {
                    slots.Add(child);
                }
            }

            if (slots.Count == 0)
            {
                return bounds.Size;
            }

            var width = bounds.Width > 0 ? bounds.Width : _grid.MinimumSlotWidth;
            var layout = _grid.Resolve(width);
            var rows = (slots.Count + layout.Columns - 1) / layout.Columns;
            var rowHeight = bounds.Height > 0
                ? (bounds.Height - (rows - 1) * _grid.RowSpacing) / rows
                : FallbackSlotHeight;

            if (rowHeight <= 0)
            {
                rowHeight = FallbackSlotHeight;
            }

            for (var index = 0; index < slots.Count; index++)
            {
                var column = index % layout.Columns;
                var row = index / layout.Columns;
                var x = bounds.Left + column * (layout.SlotWidth + _grid.ColumnSpacing);
                var y = bounds.Top + row * (rowHeight + _grid.RowSpacing);

                slots[index].Arrange(new Rect(x, y, layout.SlotWidth, rowHeight));
            }

            return bounds.Size;
        }
    }
}
