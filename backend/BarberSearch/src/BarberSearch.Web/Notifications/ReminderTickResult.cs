namespace BarberSearch.Web.Notifications;

/// <summary>
/// Итог одного тика напоминаний: числа для единственной строки журнала и для интеграционных
/// тестов, которым не нужно ждать таймер.
/// </summary>
/// <param name="Created">Сколько уведомлений реально вставлено (дедупликация учтена).</param>
/// <param name="Sent">Сколько сообщений принято Telegram.</param>
/// <param name="Skipped">Сколько уведомлений пропущено (канал не привязан, визит отменён).</param>
/// <param name="Failed">Сколько попыток доставки закончилось временной ошибкой.</param>
public sealed record ReminderTickResult(int Created, int Sent, int Skipped, int Failed)
{
    /// <summary>
    /// Пустой тик — например, когда воркер выключен (<c>Reminders:Enabled=false</c>).
    /// </summary>
    public static ReminderTickResult Empty { get; } = new(0, 0, 0, 0);
}
