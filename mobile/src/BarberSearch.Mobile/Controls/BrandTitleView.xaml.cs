namespace BarberSearch.Mobile.Controls;

/// <summary>
/// Заголовок навигационной панели с ножницами бренда.
/// </summary>
/// <remarks>
/// Shell не даёт поставить одну шапку на все вкладки, поэтому вид ставится в
/// <c>Shell.TitleView</c> каждой корневой страницы. Текст — название раздела, то же, что
/// в <c>Title</c>: смена языка пересобирает Shell, и оба значения приходят из каталога.
/// </remarks>
public partial class BrandTitleView : ContentView
{
    /// <summary>
    /// Биндинг-свойство <see cref="Text"/>.
    /// </summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(BrandTitleView),
            string.Empty,
            propertyChanged: OnTextChanged);

    /// <summary>
    /// Создаёт заголовок с пустым текстом.
    /// </summary>
    public BrandTitleView() => InitializeComponent();

    /// <summary>
    /// Название раздела рядом со знаком.
    /// </summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((BrandTitleView)bindable).TitleLabel.Text = newValue as string ?? string.Empty;
}
