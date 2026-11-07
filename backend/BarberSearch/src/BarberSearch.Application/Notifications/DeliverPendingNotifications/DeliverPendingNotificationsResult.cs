namespace BarberSearch.Application.Notifications.DeliverPendingNotifications;

/// <summary>
/// Итог тика доставки: сколько сообщений ушло, пропущено и не удалось.
/// </summary>
/// <remarks>
/// Воркер пишет эти три числа одной строкой лога на тик. <c>Failed</c> означает временную
/// ошибку: уведомление осталось <c>Pending</c> и будет повторено по расписанию 1/5/15 минут,
/// пока попыток не станет пять.
/// </remarks>
/// <param name="Sent">Сколько сообщений принято Telegram.</param>
/// <param name="Skipped">Сколько уведомлений пропущено (канал не привязан, визит отменён).</param>
/// <param name="Failed">Сколько попыток закончилось временной ошибкой.</param>
public sealed record DeliverPendingNotificationsResult(int Sent, int Skipped, int Failed);
