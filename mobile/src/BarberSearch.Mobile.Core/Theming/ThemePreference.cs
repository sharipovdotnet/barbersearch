namespace BarberSearch.Mobile.Core.Theming;

/// <summary>
/// Тема оформления приложения.
/// </summary>
/// <remarks>
/// Тема — такая же настройка устройства, как и язык: Core не знает про MAUI и не спрашивает
/// <c>Application.Current.RequestedTheme</c>. Приложение переводит этот выбор в
/// <c>Application.UserAppTheme</c>, а страницы красят токены через <c>AppThemeBinding</c>;
/// Core хранит только выбор пользователя.
/// </remarks>
public enum ThemePreference
{
    /// <summary>
    /// Как в операционной системе — выбор по умолчанию.
    /// </summary>
    /// <remarks>
    /// Именно тёмная/светлая тема ОС, а не её «системные» цвета: пока пользователь не выбрал
    /// явно, приложение следует за настройкой телефона. Нулевое значение — чтобы забытое
    /// поле настройки означало «следовать ОС», а не светлую тему.
    /// </remarks>
    System,

    /// <summary>
    /// Светлая тема независимо от настройки ОС.
    /// </summary>
    Light,

    /// <summary>
    /// Тёмная тема независимо от настройки ОС.
    /// </summary>
    Dark,
}

/// <summary>
/// Соглашения темы: код для хранения и разбор сохранённого значения.
/// </summary>
/// <remarks>
/// <para>
/// Коды <c>system</c>, <c>light</c> и <c>dark</c> — стабильные значения хранилища. Они не
/// зависят от имён членов перечисления и имён MAUI (<c>AppTheme.Unspecified</c>): переименование
/// типа или члена не должно стирать выбор пользователя при обновлении приложения.
/// </para>
/// <para>
/// Неизвестное значение — это не ошибка пользователя, а устаревшее или испорченное хранилище.
/// Поэтому разбор возвращает <see cref="ThemePreference.System"/> («как в ОС»), а не
/// <see cref="ThemePreference.Light"/>: светлая тема — такой же явный выбор, как и тёмная,
/// и подменять ею отсутствующий выбор значит игнорировать системную настройку.
/// </para>
/// </remarks>
public static class ThemePreferences
{
    /// <summary>
    /// Ключ выбора темы. Имя построено по образцу языкового ключа веба
    /// (<c>barbersearch.locale</c>), но сам ключ мобильный: в вебе переключателя темы
    /// пока нет. Хранилище у приложения своё в любом случае.
    /// </summary>
    public const string StorageKey = "barbersearch.theme";

    /// <summary>
    /// Тема по умолчанию: следовать за операционной системой.
    /// </summary>
    /// <remarks>
    /// Явный выбор по умолчанию, а не «первое, что попадётся»: приложение и тесты обязаны
    /// получать одинаковый ответ, когда пользователь ещё ничего не выбирал.
    /// </remarks>
    public static ThemePreference Default => ThemePreference.System;

    /// <summary>
    /// Код темы для хранения и диагностики: <c>system</c>, <c>light</c> или <c>dark</c>.
    /// </summary>
    /// <param name="preference">Выбранная тема.</param>
    /// <returns>Стабильный код для хранилища.</returns>
    public static string Code(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => "system",
    };

    /// <summary>
    /// Разбирает сохранённое значение; всё неизвестное считается темой системы.
    /// </summary>
    /// <param name="value">Сохранённый код.</param>
    /// <param name="preference">Распознанная тема; при ошибке — тема системы.</param>
    /// <returns><c>true</c>, если код известен.</returns>
    public static bool TryParse(string? value, out ThemePreference preference)
    {
        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
        {
            preference = ThemePreference.Light;
            return true;
        }

        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            preference = ThemePreference.Dark;
            return true;
        }

        preference = ThemePreference.System;
        return string.Equals(value, "system", StringComparison.OrdinalIgnoreCase);
    }
}
