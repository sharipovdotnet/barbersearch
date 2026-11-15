using BarberSearch.Mobile.Core.Theming;

namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Переключатель темы «Системная / Светлая / Тёмная» на вкладке «Профиль».
/// </summary>
/// <remarks>
/// <para>
/// В вебе тёмные токены описаны, но переключателя нет (frontend/AGENT.md §7.4, вопрос
/// «default to system preference, light, or no toggle?»). На телефоне выбор живёт здесь:
/// «Профиль» — единственный экран настроек, как и у переключателя языка, и по умолчанию
/// приложение следует системе.
/// </para>
/// <para>
/// Смена темы не пересобирает Shell: <c>AppThemeBinding</c> перекрашивает страницы на
/// месте, потому что строки не меняются. Сегменты красятся стилями из <c>Styles.xaml</c>
/// через <see cref="SegmentedButtonStyle"/>, поэтому подсветка выбранного сегмента следует
/// и за явным выбором, и за темой системы.
/// </para>
/// </remarks>
public partial class ThemeSwitcher : ContentView
{
    private ThemeService? _themes;

    /// <summary>
    /// Создаёт переключатель.
    /// </summary>
    public ThemeSwitcher()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Подключает сервис темы и подсвечивает сохранённый выбор.
    /// </summary>
    /// <param name="themes">Сервис, который запоминает и применяет тему.</param>
    public void Initialize(ThemeService themes)
    {
        ArgumentNullException.ThrowIfNull(themes);

        _themes = themes;
        UpdateSelection();
    }

    /// <summary>
    /// Восстанавливает подсветку при повторном появлении на странице.
    /// </summary>
    protected override void OnParentSet()
    {
        base.OnParentSet();

        UpdateSelection();
    }

    private void OnSystemClicked(object? sender, EventArgs e) => Select(ThemePreference.System);

    private void OnLightClicked(object? sender, EventArgs e) => Select(ThemePreference.Light);

    private void OnDarkClicked(object? sender, EventArgs e) => Select(ThemePreference.Dark);

    private void Select(ThemePreference preference)
    {
        if (_themes is null || _themes.Preference == preference)
        {
            return;
        }

        _themes.Select(preference);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var preference = _themes?.Preference ?? ThemePreferences.Default;

        SegmentedButtonStyle.Apply(SystemButton, preference == ThemePreference.System);
        SegmentedButtonStyle.Apply(LightButton, preference == ThemePreference.Light);
        SegmentedButtonStyle.Apply(DarkButton, preference == ThemePreference.Dark);
    }
}
