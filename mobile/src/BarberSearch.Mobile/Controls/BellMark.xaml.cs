namespace BarberSearch.Mobile.Controls;

/// <summary>
/// Колокольчик уведомлений: lucide <c>bell</c> из кабинета мастера на вебе.
/// </summary>
/// <remarks>
/// <para>
/// Знак собран геометрией, а не картинкой, по той же причине, что и
/// <see cref="ScissorsMark"/>: цвет контура приходит из темы через
/// <see cref="StrokeColor"/>, а растрированный SVG застыл бы на одном цвете.
/// </para>
/// <para>
/// В кабинете колокольчик стоит в шапке и открывает инбокс; бейдж непрочитанных —
/// соседний элемент, а не часть знака.
/// </para>
/// </remarks>
public partial class BellMark : ContentView
{
    /// <summary>
    /// Биндинг-свойство <see cref="StrokeColor"/>.
    /// </summary>
    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(
            nameof(StrokeColor),
            typeof(Color),
            typeof(BellMark),
            propertyChanged: OnStrokeColorChanged);

    /// <summary>
    /// Создаёт колокольчик.
    /// </summary>
    public BellMark() => InitializeComponent();

    /// <summary>
    /// Цвет контура; <c>null</c> — знак не рисуется.
    /// </summary>
    public Color? StrokeColor
    {
        get => (Color?)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    private static void OnStrokeColorChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var mark = (BellMark)bindable;
        var color = newValue as Color ?? Colors.Transparent;
        mark.Mark.Stroke = new SolidColorBrush(color);
    }
}
