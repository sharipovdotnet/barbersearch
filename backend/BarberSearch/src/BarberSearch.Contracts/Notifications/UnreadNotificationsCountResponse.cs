namespace BarberSearch.Contracts.Notifications;

/// <summary>
/// Число непрочитанных уведомлений для бейджа колокольчика.
/// </summary>
/// <remarks>
/// Отдаётся точное число, а не «99+»: усечение — дело клиента, и оно должно быть одним
/// и тем же в бейдже и в <c>aria-label</c>. Счётчик читается на каждом монтировании
/// кабинета, поэтому ответ намеренно минимальный.
/// </remarks>
/// <param name="Count">Сколько непрочитанных уведомлений у барбера.</param>
public record UnreadNotificationsCountResponse(int Count);
