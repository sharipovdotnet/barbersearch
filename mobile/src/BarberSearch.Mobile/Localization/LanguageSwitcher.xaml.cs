using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Theming;
using Locale = BarberSearch.Mobile.Core.Localization.Locale;

namespace BarberSearch.Mobile.Localization;

/// <summary>
/// Переключатель языка RU/EN на вкладке «Профиль».
/// </summary>
/// <remarks>
/// <para>
/// В вебе переключатель стоит в шапке; на телефоне шапки нет, а «Профиль» — единственный
/// экран настроек, поэтому пара кнопок живёт там. Это мобильное решение записано в
/// <c>AGENT.md</c> §5.
/// </para>
/// <para>
/// Смена языка пересобирает Shell: подписи в уже собранных ViewModel — обычные строки, и
/// обновить их точечно дороже, чем создать экран заново. Выбранный язык сохраняется в
/// настройках, поэтому перезапуск приложения его не сбрасывает.
/// </para>
/// </remarks>
public partial class LanguageSwitcher : ContentView
{
    private bool _isSwitching;

    /// <summary>
    /// Создаёт переключатель и подсвечивает текущий язык.
    /// </summary>
    public LanguageSwitcher()
    {
        InitializeComponent();

        UpdateSelection();
    }

    /// <summary>
    /// Подсвечивает текущий язык при появлении на странице.
    /// </summary>
    protected override void OnParentSet()
    {
        base.OnParentSet();

        UpdateSelection();
    }

    private async void OnRussianClicked(object? sender, EventArgs e) =>
        await SwitchAsync(Locale.Ru);

    private async void OnEnglishClicked(object? sender, EventArgs e) =>
        await SwitchAsync(Locale.En);

    private async Task SwitchAsync(Locale locale)
    {
        if (_isSwitching || Loc.Locale == locale)
        {
            return;
        }

        _isSwitching = true;

        try
        {
            await Loc.Current.SetLocaleAsync(locale);
            await ShellReloader.RebuildAsync();
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private void UpdateSelection()
    {
        var isRussian = Loc.Locale == Locale.Ru;

        StyleButton(RussianButton, isRussian);
        StyleButton(EnglishButton, !isRussian);
    }

    private static void StyleButton(Button button, bool isSelected) =>
        SegmentedButtonStyle.Apply(button, isSelected);
}
