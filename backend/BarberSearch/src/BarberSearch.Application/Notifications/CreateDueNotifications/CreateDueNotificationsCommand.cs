using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.CreateDueNotifications;

/// <summary>
/// Команда тика создания уведомлений: напоминания о визитах и утренние дайджесты.
/// </summary>
/// <remarks>
/// Параметров нет: тик обслуживает всех барберов с включёнными напоминаниями. Команду
/// вызывает фоновый воркер, а не клиент, поэтому ни пользователя, ни салона в ней нет.
/// </remarks>
public sealed record CreateDueNotificationsCommand : ICommand;
