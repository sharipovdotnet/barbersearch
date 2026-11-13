using BarberSearch.Contracts.BarberProfiles.BarberSchedule;

namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Результат пишущей ручки расписания: сохранено или заблокировано списком визитов.
/// </summary>
/// <remarks>
/// Отдельный тип нужен потому, что <c>409</c> — не доменная ошибка с одним кодом, а ответ
/// со списком затронутых броней: <c>Failure</c> умеет только коды. Пустой список означает
/// успех, и контроллер отвечает <c>204</c>.
/// </remarks>
/// <param name="Conflicts">Затронутые визиты; пусто — правка сохранена.</param>
public sealed record ScheduleWriteOutcome(IReadOnlyList<ScheduleConflictResponse> Conflicts)
{
    /// <summary>
    /// Правка сохранена, затронутых визитов нет.
    /// </summary>
    public static ScheduleWriteOutcome Saved { get; } = new([]);

    /// <summary>
    /// Правка не сохранена: перечисленные визиты выйдут за новые часы или попадут в отпуск.
    /// </summary>
    /// <param name="conflicts">Непустой список затронутых визитов.</param>
    public static ScheduleWriteOutcome Blocked(IReadOnlyList<ScheduleConflictResponse> conflicts)
        => new(conflicts);
}
