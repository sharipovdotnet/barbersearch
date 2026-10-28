namespace BarberSearch.Application.BarberProfiles.Admin;

/// <summary>
/// Фильтр админского списка барберов.
/// </summary>
/// <remarks>
/// <see cref="All"/> — все профили, <see cref="Pending"/> — неподтверждённые (очередь проверки,
/// старые сверху), <see cref="Hidden"/> — выключенные из поиска. Направление сортировки задаёт
/// статус: проверка идёт FIFO, остальные списки — свежими сверху.
/// </remarks>
public enum AdminBarberStatus
{
    /// <summary>Все профили.</summary>
    All = 0,

    /// <summary>Неподтверждённые профили — очередь проверки.</summary>
    Pending = 1,

    /// <summary>Профили, скрытые из поиска.</summary>
    Hidden = 2
}
