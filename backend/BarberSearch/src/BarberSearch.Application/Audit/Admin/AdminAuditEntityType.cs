namespace BarberSearch.Application.Audit.Admin;

/// <summary>
/// Тип сущности в фильтре журнала изменений.
/// </summary>
/// <remarks>
/// Значения ровно три — это allowlist <c>AuditTrail</c>: <c>User</c>, <c>BarberProfile</c>
/// и <c>Review</c>. Каталог, салоны, услуги, специализации и брони в журнал не попадают,
/// поэтому и фильтра для них нет. <see cref="All"/> — отсутствие фильтра.
/// </remarks>
public enum AdminAuditEntityType
{
    /// <summary>
    /// Без фильтра: все типы, которые аудируются.
    /// </summary>
    All = 0,

    /// <summary>
    /// Пользователь: роль, доступ, подтверждения, email, профиль барбера.
    /// </summary>
    User = 1,

    /// <summary>
    /// Профиль барбера: грейд, активность, подтверждение, рейтинг.
    /// </summary>
    BarberProfile = 2,

    /// <summary>
    /// Отзыв: публикация, рейтинг, текст.
    /// </summary>
    Review = 3
}
