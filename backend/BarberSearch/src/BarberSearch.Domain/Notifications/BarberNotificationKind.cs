namespace BarberSearch.Domain.Notifications;

/// <summary>
/// Вид напоминания барберу.
/// </summary>
/// <remarks>
/// Значения явные (AGENT.md §10.10) и попадают в базу как <c>smallint</c>: по ним стоят
/// частичные уникальные индексы (<c>kind = 1</c> — напоминание о визите, <c>kind = 2</c> —
/// утренний дайджест), поэтому числовые значения менять нельзя.
/// </remarks>
public enum BarberNotificationKind
{
    /// <summary>
    /// Напоминание о конкретном визите: создаётся за <c>LeadMinutes</c> до начала.
    /// </summary>
    Upcoming = 1,

    /// <summary>
    /// Утренний дайджест: одно сообщение в день на барбера со числом записей и первой записью.
    /// </summary>
    Digest = 2
}
