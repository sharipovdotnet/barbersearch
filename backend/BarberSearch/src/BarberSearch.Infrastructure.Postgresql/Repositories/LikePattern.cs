namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Шаблон <c>ILIKE</c> для поиска по названию.
/// </summary>
/// <remarks>
/// <c>%</c>, <c>_</c> и <c>\</c> из пользовательского запроса экранируются: иначе поиск
/// «100%» находил бы всё, а «_» работал бы как любой одиночный символ. Тот же приём, что
/// в админских чтениях пользователей и барберов, но без привязки к конкретному репозиторию:
/// каталог ищет по названию тремя разными запросами.
/// </remarks>
internal static class LikePattern
{
    /// <summary>
    /// Шаблон «содержит подстроку» с экранированными служебными символами.
    /// </summary>
    /// <param name="search">Пользовательская строка поиска.</param>
    /// <returns>Шаблон вида <c>%строка%</c>, безопасный для <c>ILIKE</c> с <c>ESCAPE '\'</c>.</returns>
    public static string Contains(string search)
        => "%" + search
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
        + "%";
}
