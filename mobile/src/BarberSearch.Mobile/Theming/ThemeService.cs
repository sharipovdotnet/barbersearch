using BarberSearch.Mobile.Core.Theming;
using Microsoft.Maui.ApplicationModel;

namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Применяет выбранную тему к приложению и запоминает выбор.
/// </summary>
/// <remarks>
/// <para>
/// Тема — настройка устройства, а не аккаунта: она живёт в настройках платформы и не уходит
/// на сервер. По умолчанию — системная: приложение следует теме телефона, пока человек
/// не выбрал светлую или тёмную явно.
/// </para>
/// <para>
/// <see cref="Application.UserAppTheme"/> меняет <see cref="Application.RequestedTheme"/>, и
/// <c>AppThemeBinding</c> перекрашивает все элементы на месте. Пересобирать Shell, в отличие
/// от смены языка, не нужно: строки не меняются, меняется только палитра.
/// </para>
/// <para>
/// Платформенная оболочка не знает про <c>AppThemeBinding</c>, поэтому та же тема применяется
/// к системным панелям через <see cref="PlatformTheme"/> — и при старте, и после смены.
/// </para>
/// </remarks>
public sealed class ThemeService
{
    private readonly IThemeStore _store;

    /// <summary>
    /// Создаёт сервис поверх хранилища выбранной темы.
    /// </summary>
    /// <param name="store">Хранилище выбора; в приложении — настройки платформы.</param>
    public ThemeService(IThemeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <summary>
    /// Сохранённый выбор темы.
    /// </summary>
    public ThemePreference Preference => _store.Read();

    /// <summary>
    /// Применяет сохранённую тему при старте приложения.
    /// </summary>
    public void ApplyStored() => Apply(Preference);

    /// <summary>
    /// Запоминает выбор и применяет его немедленно.
    /// </summary>
    /// <param name="preference">Системная, светлая или тёмная тема.</param>
    public void Select(ThemePreference preference)
    {
        _store.Write(preference);
        Apply(preference);
    }

    private static void Apply(ThemePreference preference)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.UserAppTheme = preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        // RequestedTheme уже учитывает и явный выбор, и тему системы: красим панели по нему,
        // а не по preference, иначе «Системная» на тёмном телефоне дала бы светлый статус-бар.
        PlatformTheme.Apply(application.RequestedTheme == AppTheme.Dark);
    }
}
