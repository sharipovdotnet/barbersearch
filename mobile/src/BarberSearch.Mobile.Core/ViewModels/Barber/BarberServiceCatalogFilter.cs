namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Проверяет название услуги справочника на совпадение с поисковой строкой.
/// </summary>
/// <remarks>
/// <para>
/// Поиск локальный: справочник уже прочитан и лежит в памяти, поэтому фильтр — это подстрока
/// названия без учёта регистра. Запроса и задержки на ввод нет: пересчёт десятков строк дешевле
/// одного кадра, и мастер видит результат на каждой букве.
/// </para>
/// <para>
/// «ё» и «е» для мастера одна буква: в справочнике стоит «Окантовка и бритьё шеи», а набирают
/// «бритье». Обе строки приводятся к «е» до сравнения. Сравнение ординальное: это поиск
/// подстроки, а не сортировка, и свёртка локали здесь только смешала бы регистр с алфавитом.
/// </para>
/// </remarks>
public static class BarberServiceCatalogFilter
{
    /// <summary>
    /// Строка поиска без крайних пробелов и с «ё», приведённой к «е».
    /// </summary>
    /// <param name="value">Строка поиска или название услуги.</param>
    /// <returns>Строка для сравнения; пустая, если искать нечего.</returns>
    /// <remarks>
    /// Пробелы в начале и конце не считаются запросом: случайно поставленный пробел не должен
    /// прятать весь справочник под «Ничего не найдено.».
    /// </remarks>
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Replace('ё', 'е').Replace('Ё', 'Е');

    /// <summary>
    /// Искать нечего: пустая строка или одни пробелы.
    /// </summary>
    /// <param name="query">Строка поиска.</param>
    /// <returns><c>true</c>, если фильтр пропускает все строки.</returns>
    public static bool IsEmpty(string? query) => Normalize(query).Length == 0;

    /// <summary>
    /// Название подходит под запрос.
    /// </summary>
    /// <param name="name">Название услуги.</param>
    /// <param name="query">Строка поиска; пустая пропускает всё.</param>
    /// <returns><c>true</c>, если запрос — подстрока названия.</returns>
    public static bool Matches(string name, string? query)
    {
        var normalizedQuery = Normalize(query);

        return normalizedQuery.Length == 0
               || Normalize(name).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }
}
