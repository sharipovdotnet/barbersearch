namespace BarberSearch.Application.Common;

/// <summary>
/// Фильтр статуса админского каталога: услуги, специализации и салоны.
/// </summary>
/// <remarks>
/// <see cref="All"/> — полный справочник, включая выключенные строки; это значение
/// по умолчанию, иначе выключенную строку нельзя найти, чтобы включить обратно.
/// <see cref="Active"/> — строки, видимые витрине; <see cref="Hidden"/> — выключенные.
/// </remarks>
public enum AdminCatalogueStatus
{
    /// <summary>Все строки справочника, включая выключенные.</summary>
    All = 0,

    /// <summary>Только включённые строки.</summary>
    Active = 1,

    /// <summary>Только выключенные строки.</summary>
    Hidden = 2
}

/// <summary>
/// Разбор параметров админского чтения каталога.
/// </summary>
/// <remarks>
/// Общий разбор для трёх справочников: у них одинаковые параметры и одинаковые коды ошибок,
/// поэтому правила живут в одном месте, а не дублируются в трёх обработчиках. Неизвестный
/// статус возвращает <c>null</c> — обработчик отвечает <c>400 admin.catalogue.status</c>;
/// <c>Enum.TryParse</c> здесь не используется, потому что он принимает и числа
/// (<c>"0"</c> — это <c>All</c>), а публичный контракт обещает имена.
/// </remarks>
public static class AdminCatalogueFilters
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    public const int MAX_CATALOGUE_SEARCH_LENGTH = 100;

    /// <summary>
    /// Разбирает статус: <c>null</c> и пустая строка — <see cref="AdminCatalogueStatus.All"/>.
    /// </summary>
    /// <param name="status">Значение параметра <c>status</c>.</param>
    /// <returns>Статус фильтра либо <c>null</c>, если значение неизвестно.</returns>
    public static AdminCatalogueStatus? ParseStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => AdminCatalogueStatus.All,
            "active" => AdminCatalogueStatus.Active,
            "hidden" => AdminCatalogueStatus.Hidden,
            _ => null
        };
}
