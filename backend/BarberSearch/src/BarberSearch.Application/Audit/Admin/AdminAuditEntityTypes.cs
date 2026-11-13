namespace BarberSearch.Application.Audit.Admin;

/// <summary>
/// Разбор и имена значений <see cref="AdminAuditEntityType"/>.
/// </summary>
/// <remarks>
/// Наружу и в <c>audit_log</c> типы ходят стабильными строками (<c>user</c>,
/// <c>barber_profile</c>, <c>review</c>, <c>salon</c>, <c>hair_service</c>,
/// <c>specialization</c>, <c>booking</c>, <c>portfolio_photo</c>, <c>barber_service</c>),
/// а не именами CLR-типов и не числами: переименование класса не должно менять историю.
/// Разбор нечувствителен к регистру и обрезает пробелы по краям, но числа (<c>0</c>,
/// <c>9</c>) отвергаются — иначе клиент мог бы прислать «тип» числом.
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
    /// Стабильное имя салона в журнале.
    /// </summary>
    public const string SALON = "salon";

    /// <summary>
    /// Стабильное имя услуги каталога в журнале.
    /// </summary>
    public const string HAIR_SERVICE = "hair_service";

    /// <summary>
    /// Стабильное имя специализации в журнале.
    /// </summary>
    public const string SPECIALIZATION = "specialization";

    /// <summary>
    /// Стабильное имя брони в журнале.
    /// </summary>
    public const string BOOKING = "booking";

    /// <summary>
    /// Стабильное имя фотографии портфолио в журнале.
    /// </summary>
    public const string PORTFOLIO_PHOTO = "portfolio_photo";

    /// <summary>
    /// Стабильное имя прайса барбера в журнале.
    /// </summary>
    public const string BARBER_SERVICE = "barber_service";

    /// <summary>
    /// Разбирает параметр <c>entityType</c>: <c>null</c>, пустая строка и <c>all</c> —
    /// отсутствие фильтра; имена принимаются в любом регистре после <c>Trim</c>.
    /// </summary>
    /// <remarks>
    /// Разбор строковый, а не <c>Enum.TryParse</c>: последний принял бы <c>"4"</c> за
    /// <see cref="AdminAuditEntityType.Salon"/>, и число в query string стало бы фильтром.
    /// </remarks>
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

        if (trimmed.Equals(SALON, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.Salon;
            return true;
        }

        if (trimmed.Equals(HAIR_SERVICE, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.HairService;
            return true;
        }

        if (trimmed.Equals(SPECIALIZATION, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.Specialization;
            return true;
        }

        if (trimmed.Equals(BOOKING, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.Booking;
            return true;
        }

        if (trimmed.Equals(PORTFOLIO_PHOTO, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.PortfolioPhoto;
            return true;
        }

        if (trimmed.Equals(BARBER_SERVICE, StringComparison.OrdinalIgnoreCase))
        {
            entityType = AdminAuditEntityType.BarberService;
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
            AdminAuditEntityType.Salon => SALON,
            AdminAuditEntityType.HairService => HAIR_SERVICE,
            AdminAuditEntityType.Specialization => SPECIALIZATION,
            AdminAuditEntityType.Booking => BOOKING,
            AdminAuditEntityType.PortfolioPhoto => PORTFOLIO_PHOTO,
            AdminAuditEntityType.BarberService => BARBER_SERVICE,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entityType),
                entityType,
                "Only the audited entity types have a storage name.")
        };
}
