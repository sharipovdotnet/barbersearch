using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Хранилище состояния push в настройках платформы.
/// </summary>
/// <remarks>
/// Ключи <c>barbersearch.push.*</c> повторяют схему языка и темы (<c>barbersearch.locale</c>,
/// <c>barbersearch.theme</c>). Ошибка чтения или записи не роняет экран: выбор остаётся
/// в памяти до перезапуска — так же поступает <c>PreferencesThemeStore</c>, и падение
/// из-за недоступных настроек было бы хуже потерянного переключателя.
/// </remarks>
public sealed class PreferencesPushStateStore : IPushStateStore
{
    /// <summary>
    /// Ключ переключателя «Присылать напоминания».
    /// </summary>
    public const string EnabledKey = "barbersearch.push.enabled";

    /// <summary>
    /// Ключ последнего зарегистрированного токена устройства.
    /// </summary>
    public const string TokenKey = "barbersearch.push.token";

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            try
            {
                return Preferences.Default.Get(EnabledKey, true);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <inheritdoc />
    public string? RegisteredToken
    {
        get
        {
            try
            {
                var token = Preferences.Default.Get(TokenKey, string.Empty);

                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        try
        {
            Preferences.Default.Set(EnabledKey, enabled);
        }
        catch (Exception)
        {
            // Значение остаётся в памяти до перезапуска — это лучше, чем падение.
        }
    }

    /// <inheritdoc />
    public void SetRegisteredToken(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token))
            {
                Preferences.Default.Remove(TokenKey);

                return;
            }

            Preferences.Default.Set(TokenKey, token);
        }
        catch (Exception)
        {
            // Метка не сохранилась: следующий запуск просто зарегистрирует токен заново.
        }
    }
}
