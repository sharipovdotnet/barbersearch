using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.CleanupOldNotifications;

/// <summary>
/// Команда суточной уборки инбокса: удалить уведомления старше ретенции.
/// </summary>
/// <remarks>
/// Параметров нет: граница ретенции считается от <c>TimeProvider</c> и
/// <c>Reminders:RetentionDays</c> (180 дней по умолчанию). Воркер вызывает команду раз
/// в сутки, а не каждую минуту.
/// </remarks>
public sealed record CleanupOldNotificationsCommand : ICommand;
