namespace BarberSearch.Mobile.Core.Location;

/// <summary>
/// Приводит название города от геокодера к виду, которым фильтрует сервер.
/// </summary>
/// <remarks>
/// Сервер сравнивает <c>Salon.City</c> точно (<c>salon.City == city</c>), поэтому «г. Москва»
/// из обратного геокодирования не нашёл бы ни одного салона. Приставки, которые добавляют
/// платформенные геокодеры, снимаются здесь, а пустая строка становится <c>null</c>:
/// решение «подставить город по умолчанию» принимает экран, а не эта функция.
/// </remarks>
public static class CityName
{
    /// <summary>
    /// Приставки, которые геокодеры добавляют к названию города.
    /// </summary>
    /// <remarks>
    /// Однобуквенное «г» без точки не снимается: «Гатчина» и «Грозный» начинаются с той же
    /// буквы, и отличить приставку от названия было бы нельзя.
    /// </remarks>
    private static readonly string[] Prefixes = ["г.", "гор.", "город "];

    /// <summary>
    /// Убирает приставку «г.»/«гор.»/«город» и лишние пробелы.
    /// </summary>
    /// <param name="value">Название от геокодера; может быть <c>null</c>.</param>
    /// <returns>Голое название города либо <c>null</c>, если названия нет.</returns>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var city = value.Trim();

        foreach (var prefix in Prefixes)
        {
            if (city.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                city = city[prefix.Length..].TrimStart();
                break;
            }
        }

        return city.Length == 0 ? null : city;
    }
}
