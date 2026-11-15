using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Push.DeliverPendingPushNotifications;

/// <summary>
/// Команда тика доставки: забрать пачку push-напоминаний и отправить их на устройства.
/// </summary>
/// <remarks>
/// Параметров нет: тик обслуживает очередь всех клиентов, а размер пачки приходит из
/// <c>Push:BatchSize</c>. Команду вызывает фоновый воркер.
/// </remarks>
public sealed record DeliverPendingPushNotificationsCommand : ICommand;
