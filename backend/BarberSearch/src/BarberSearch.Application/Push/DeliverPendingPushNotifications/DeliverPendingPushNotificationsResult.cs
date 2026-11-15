namespace BarberSearch.Application.Push.DeliverPendingPushNotifications;

/// <summary>
/// Итог тика доставки push-напоминаний.
/// </summary>
/// <remarks>
/// <c>Sent</c> — сообщение приняло хотя бы одно устройство; <c>Skipped</c> — доставка
/// не нужна (визит отменён или перенесён, окно закрылось, устройств нет, push не настроен);
/// <c>Failed</c> — попытка сорвалась и будет повторена (или уже исчерпана).
/// </remarks>
/// <param name="Sent">Сколько напоминаний доставлено.</param>
/// <param name="Skipped">Сколько пропущено окончательно.</param>
/// <param name="Failed">Сколько неудачных попыток записано.</param>
public sealed record DeliverPendingPushNotificationsResult(int Sent, int Skipped, int Failed);
