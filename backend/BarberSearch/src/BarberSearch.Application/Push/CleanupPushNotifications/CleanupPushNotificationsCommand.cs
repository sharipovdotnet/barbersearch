using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Push.CleanupPushNotifications;

/// <summary>
/// Команда суточной уборки push-очереди и токенов устройств.
/// </summary>
/// <remarks>
/// Параметров нет: границы ретенции берутся из <c>Push:RetentionDays</c> и
/// <c>Push:DeviceTokenRetentionDays</c>. Команду вызывает фоновый воркер не чаще раза в сутки.
/// </remarks>
public sealed record CleanupPushNotificationsCommand : ICommand;
