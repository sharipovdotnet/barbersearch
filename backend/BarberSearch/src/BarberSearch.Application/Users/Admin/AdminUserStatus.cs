namespace BarberSearch.Application.Users.Admin;

/// <summary>
/// Фильтр админского списка пользователей.
/// </summary>
/// <remarks>
/// <see cref="All"/> — все, <see cref="Active"/> — с открытым доступом, <see cref="Blocked"/> —
/// заблокированные, <see cref="Unverified"/> — с неподтверждённым email. Порядок одинаковый
/// для всех фильтров: свежие сверху, как у опубликованных отзывов и списка барберов.
/// </remarks>
public enum AdminUserStatus
{
    /// <summary>Все пользователи.</summary>
    All = 0,

    /// <summary>Пользователи с открытым доступом.</summary>
    Active = 1,

    /// <summary>Заблокированные пользователи.</summary>
    Blocked = 2,

    /// <summary>Пользователи без подтверждённого email.</summary>
    Unverified = 3
}
