namespace BarberSearch.Application.Audit.Admin;

/// <summary>
/// Разбор и имена значений <see cref="AdminAuditEntityType"/>.
/// </summary>
/// <remarks>
/// Наружу и в <c>audit_log</c> типы ходят стабильными строками (<c>user</c>,
/// <c>barber_profile</c>, <c>review</c>), а не именами CLR-типов и не числами: переименование
/// класса не должно менять историю. Разбор нечувствителен к регистру, но числа
/// (<c>0</c>, <c>2</c>) отвергаются — иначе клиент мог бы прислать «тип» числом.
/// </remarks>
public static class AdminAuditEntityTypes
{
    /// <summary>
    /// Стабильное имя пользователя в журнале.
    /// </summary>
    public const string USER = "user";

    /// <summary>
    /// Стабильное имя профиля барбера в журнале.
    /// </summary>
    public const string BARBER_PROFILE = "barber_profile";

    /// <summary>
    /// Стабильное имя отзыва в журнале.
    /// </summary>
    public const string REVIEW = "review";

    /// <summary>
    /// Разбирает параметр <c>entityType</c>: <c>null</c>, пустая строка и <c>all</c> —
    /// отсутствие фильтра; имена принимаются в любом регистре.
    /// </summary>
    /// <param name="value">Значение из query string.</param>
    /// <param name="entityType">Разобранный тип.</param>
    public static bool TryParse(string? value, out AdminAuditEntityType entityType)
    {
        entityType = AdminAuditEntityType.All;

        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Equals(USER, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.User;
            return true;
        }

        if (trimmed.Equals(BARBER_PROFILE, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.BarberProfile;
            return true;
        }

        if (trimmed.Equals(REVIEW, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.Review;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Стабильное имя типа для фильтра по <c>audit_log.entity_type</c>.
    /// </summary>
    /// <param name="entityType">Разобранный тип; <see cref="AdminAuditEntityType.All"/> недопустим.</param>
    public static string ToStorageName(AdminAuditEntityType entityType)
        => entityType switch
        {
            AdminAuditEntityType.User => USER,
            AdminAuditEntityType.BarberProfile => BARBER_PROFILE,
            AdminAuditEntityType.Review => REVIEW,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entityType),
                entityType,
                "Only the audited entity types have a storage name.")
        };
}
