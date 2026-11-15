using System.Globalization;

namespace BarberSearch.Mobile.Core.Localization;

/// <summary>
/// Языки интерфейса приложения.
/// </summary>
/// <remarks>
/// Набор тот же, что в вебе (<c>frontend/src/i18n/locale.ts</c>): русский и английский.
/// По умолчанию — русский, как и на сайте.
/// </remarks>
public enum Locale
{
    /// <summary>
    /// Русский язык — язык исходных строк.
    /// </summary>
    Ru,

    /// <summary>
    /// Английский язык — перевод из каталога.
    /// </summary>
    En,
}

/// <summary>
/// Языковые соглашения: культура .NET, код и хранение выбора.
/// </summary>
public static class Locales
{
    /// <summary>
    /// Ключ выбора языка. Тот же, что в вебе, — это удобно при отладке и не мешает
    /// платформам: у приложения собственное хранилище настроек.
    /// </summary>
    public const string StorageKey = "barbersearch.locale";

    /// <summary>
    /// Культура показа дат, чисел и цен.
    /// </summary>
    /// <remarks>
    /// Английский — <c>en-GB</c>, а не <c>en-US</c>: в интерфейсе круглосуточное время и
    /// европейский порядок даты, как в русской версии, а не AM/PM. Это правило веба
    /// (<c>localeTag</c>) повторено здесь, чтобы оба клиента показывали одну дату одинаково.
    /// </remarks>
    /// <param name="locale">Язык интерфейса.</param>
    /// <returns>Культура для форматтеров.</returns>
    public static CultureInfo Culture(Locale locale) => locale == Locale.En
        ? CultureInfo.GetCultureInfo("en-GB")
        : CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Код языка для хранения и диагностики: <c>ru</c> или <c>en</c>.
    /// </summary>
    /// <param name="locale">Язык интерфейса.</param>
    /// <returns>Короткий код.</returns>
    public static string Code(Locale locale) => locale == Locale.En ? "en" : "ru";

    /// <summary>
    /// Разбирает сохранённое значение; всё неизвестное считается русским.
    /// </summary>
    /// <param name="value">Сохранённый код.</param>
    /// <param name="locale">Распознанный язык.</param>
    /// <returns><c>true</c>, если код известен.</returns>
    public static bool TryParse(string? value, out Locale locale)
    {
        if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
        {
            locale = Locale.En;
            return true;
        }

        locale = Locale.Ru;
        return string.Equals(value, "ru", StringComparison.OrdinalIgnoreCase);
    }
}
