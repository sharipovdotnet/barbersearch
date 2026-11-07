namespace BarberSearch.Application.Notifications.CleanupOldNotifications;

/// <summary>
/// Итог суточной уборки инбокса.
/// </summary>
/// <remarks>
/// Число удалённых строк попадает в строку лога воркера: по нему видно, работает ли
/// ретенция вообще, без обращения к базе.
/// </remarks>
/// <param name="Deleted">Сколько уведомлений удалено.</param>
public sealed record CleanupOldNotificationsResult(int Deleted);
