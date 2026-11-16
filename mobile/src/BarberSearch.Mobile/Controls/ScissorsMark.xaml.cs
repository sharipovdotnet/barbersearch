namespace BarberSearch.Mobile.Controls;

/// <summary>
/// Знак бренда: ножницы из шапки веба.
/// </summary>
/// <remarks>
/// <para>
/// Контур — lucide <c>scissors</c> (<c>site-header.tsx</c>): те же пять фигур на холсте 24×24
/// и штрих 2. Геометрия объявлена здесь один раз: знак стоит в шапке поиска, в пустой выдаче,
/// в заголовке навигации и на входе в «Профиль».
/// </para>
/// <para>
/// Цвет контура задаётся снаружи через <see cref="StrokeColor"/>, поэтому знак перекрашивается
/// вместе с темой, как иконки табов; SVG в <c>Resources/Images</c> застыл бы на одном цвете.
/// </para>
/// </remarks>
public partial class ScissorsMark : ContentView
{
    /// <summary>
    /// Биндинг-свойство <see cref="StrokeColor"/>.
    /// </summary>
    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(
            nameof(StrokeColor),
            typeof(Color),
            typeof(ScissorsMark),
            propertyChanged: OnStrokeColorChanged);

    /// <summary>
    /// Создаёт знак с контуром веба.
    /// </summary>
    public ScissorsMark() => InitializeComponent();

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
        var mark = (ScissorsMark)bindable;
        var color = newValue as Color ?? Colors.Transparent;
        mark.Mark.Stroke = new SolidColorBrush(color);
    }
}
