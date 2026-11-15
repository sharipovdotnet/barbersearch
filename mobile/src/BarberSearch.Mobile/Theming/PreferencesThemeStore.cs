using BarberSearch.Mobile.Core.Theming;

namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Хранилище выбранной темы в настройках платформы.
/// </summary>
/// <remarks>
/// Ключ <c>barbersearch.theme</c> повторяет схему языкового (<c>barbersearch.locale</c>),
/// но хранилище своё: у приложения нет <c>localStorage</c>. Ошибка чтения или записи
/// не роняет экран — выбор остаётся в памяти до перезапуска, как и у языка.
/// </remarks>
public sealed class PreferencesThemeStore : IThemeStore
{
    /// <inheritdoc />
    public ThemePreference Read()
    {
        try
        {
            var value = Preferences.Default.Get(
                ThemePreferences.StorageKey,
                ThemePreferences.Code(ThemePreferences.Default));

            return ThemePreferences.TryParse(value, out var preference)
                ? preference
                : ThemePreferences.Default;
        }
        catch (Exception)
        {
            return ThemePreferences.Default;
        }
    }

    /// <inheritdoc />
    public void Write(ThemePreference preference)
    {
        try
        {
            Preferences.Default.Set(ThemePreferences.StorageKey, ThemePreferences.Code(preference));
        }
        catch (Exception)
        {
            // Тема остаётся в памяти до перезапуска — это лучше, чем падение.
        }
    }
}
