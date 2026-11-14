using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Пункт фильтра «Язык»: один язык общения или «Любой язык».
/// </summary>
/// <remarks>
/// Языки — перечисление API, а не справочник из сети, поэтому список строится сразу и не
/// зависит от загрузки фильтров. Подписи — самоназвания из веба
/// (<c>LANGUAGE_LABELS</c>): «Русский», «English», «Кыргызча».
/// </remarks>
public sealed class SearchLanguageOption
{
    private SearchLanguageOption(Language? value, string displayText)
    {
        Value = value;
        DisplayText = displayText;
    }

    /// <summary>
    /// Язык для запроса; <c>null</c> — «фильтра нет».
    /// </summary>
    public Language? Value { get; }

    /// <summary>
    /// Подпись в <c>Picker</c>: «Любой язык» или самоназвание языка.
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Пункт «фильтра нет» с заданной подписью.
    /// </summary>
    /// <param name="text">Подпись пункта.</param>
    /// <returns>Пункт без языка.</returns>
    public static SearchLanguageOption Any(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SearchLanguageOption(null, text);
    }

    /// <summary>
    /// Пункт с языком из перечисления API.
    /// </summary>
    /// <param name="language">Язык общения.</param>
    /// <returns>Пункт для <c>Picker</c>.</returns>
    public static SearchLanguageOption From(Language language) =>
        new(language, BarberText.LanguageText(language));
}
