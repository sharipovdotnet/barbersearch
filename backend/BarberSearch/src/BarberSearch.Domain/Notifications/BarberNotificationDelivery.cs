namespace BarberSearch.Domain.Notifications;

/// <summary>
/// Состояние доставки уведомления во внешний канал.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Pending"/> = 0 — значение по умолчанию для нового уведомления и единственное
/// состояние, из которого возможны переходы. <see cref="Sent"/>, <see cref="Skipped"/> и
/// <see cref="Failed"/> — терминальные: вернуться в <see cref="Pending"/> нельзя (AGENT.md §10.10:
/// явные числовые значения; индекс доставки фильтрует <c>delivery in (0, 3)</c>).
/// </para>
/// <para>
/// Повторная отправка после ошибки не переводит строку в отдельное состояние: пока
/// <see cref="BarberNotification.DeliveryAttempts"/> меньше предела, строка остаётся
/// <see cref="Pending"/> и лишь получает <see cref="BarberNotification.NextAttemptAt"/>.
/// </para>
/// </remarks>
public enum BarberNotificationDelivery
{
    /// <summary>
    /// Ожидает доставки; единственное состояние, допускающее переход.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Доставлено в канал.
    /// </summary>
    Sent = 1,

    /// <summary>
    /// Доставка не нужна или невозможна: канал не привязан, выключен, чат заблокирован,
    /// запись отменена. Инбокс при этом сохраняется.
    /// </summary>
    Skipped = 2,

    /// <summary>
    /// Доставка окончательно не удалась: исчерпаны попытки.
    /// </summary>
    Failed = 3
}
