namespace BarberSearch.Application.Users;

/// <summary>
/// Узкая проекция учётных данных пользователя: активность и текущий отпечаток.
/// </summary>
/// <remarks>
/// Это всё, что нужно проверке security stamp. Проекция читается одним
/// <c>SELECT is_active, security_stamp … WHERE id = …</c> — без <c>Include</c>,
/// без отслеживания и без материализации агрегата с избранным (R-11).
/// </remarks>
/// <param name="IsActive">Активен ли пользователь; деактивированный отзывается сразу.</param>
/// <param name="SecurityStamp">Текущий отпечаток состояния учётных данных.</param>
public sealed record UserSecurityStamp(bool IsActive, string SecurityStamp);
