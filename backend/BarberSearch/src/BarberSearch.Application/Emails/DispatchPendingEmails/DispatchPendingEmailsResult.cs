namespace BarberSearch.Application.Emails.DispatchPendingEmails;

/// <summary>
/// Итог одного тика диспетчера.
/// </summary>
/// <remarks>
/// Воркер пишет эти три числа одной строкой лога на тик. <c>Retried</c> — временная ошибка:
/// строка осталась в outbox и будет повторена по расписанию backoff. <c>Failed</c> —
/// попытки исчерпаны, строка получила <c>failed_at</c> и больше не отправляется.
/// </remarks>
/// <param name="Sent">Сколько писем принял транспорт.</param>
/// <param name="Retried">Сколько попыток закончилось временной ошибкой.</param>
/// <param name="Failed">Сколько строк окончательно провалено на этом тике.</param>
public sealed record DispatchPendingEmailsResult(int Sent, int Retried, int Failed)
{
    /// <summary>
    /// Пустой итог: отправлять было нечего или воркер выключен.
    /// </summary>
    public static DispatchPendingEmailsResult Empty { get; } = new(0, 0, 0);
}
