using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Push.CreateDuePushNotifications;

/// <summary>
/// Команда тика создания push-напоминаний клиентам.
/// </summary>
/// <remarks>
/// Параметров нет: тик обслуживает все подтверждённые визиты в окне напоминания. Команду
/// вызывает фоновый воркер, а не клиент.
/// </remarks>
public sealed record CreateDuePushNotificationsCommand : ICommand;
