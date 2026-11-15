using BarberSearch.Mobile.Core.Localization;
using Locale = BarberSearch.Mobile.Core.Localization.Locale;

namespace BarberSearch.Mobile.Localization;

/// <summary>
/// Хранилище выбранного языка в настройках платформы.
/// </summary>
/// <remarks>
/// Ключ тот же, что в вебе (<c>barbersearch.locale</c>), но хранилище своё: у приложения нет
/// <c>localStorage</c>. Ошибка чтения или записи не роняет экран — выбор остаётся в памяти до
/// перезапуска, как и в браузере с заблокированным хранилищем.
/// </remarks>
public sealed class PreferencesLocaleStore : ILocaleStore
{
    /// <inheritdoc />
    public Locale Read()
    {
        try
        {
            var value = Preferences.Default.Get(Locales.StorageKey, "ru");

            return Locales.TryParse(value, out var locale) ? locale : Locale.Ru;
        }
        catch (Exception)
        {
            return Locale.Ru;
        }
    }

    /// <inheritdoc />
    public void Write(Locale locale)
    {
        try
        {
            Preferences.Default.Set(Locales.StorageKey, Locales.Code(locale));
        }
        catch (Exception)
        {
            // Язык остаётся в памяти до перезапуска — это лучше, чем падение.
        }
    }
}
