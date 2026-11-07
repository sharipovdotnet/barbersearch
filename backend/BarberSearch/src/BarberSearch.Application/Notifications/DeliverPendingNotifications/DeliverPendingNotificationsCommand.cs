using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.DeliverPendingNotifications;

/// <summary>
/// Команда тика доставки: забрать пачку ожидающих уведомлений и отправить их в Telegram.
/// </summary>
/// <remarks>
/// Параметров нет: тик обслуживает очередь всех барберов, а размер пачки приходит из
/// конфигурации (<c>Reminders:BatchSize</c>). Команду вызывает фоновый воркер.
/// </remarks>
public sealed record DeliverPendingNotificationsCommand : ICommand;
