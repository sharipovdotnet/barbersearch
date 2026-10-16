namespace BarberSearch.Domain.TimeOffs;

/// <summary>
/// Отпуск / нерабочий период барбера с конкретными датами. Дочерняя сущность агрегата
/// <c>BarberProfile</c>: создаётся только методами агрегата.
/// </summary>
/// <remarks>
/// Отличается от <c>WorkingSchedule.IsDayOff</c>: там речь про день недели,
/// здесь — про конкретный интервал дат.
/// </remarks>
public class TimeOff : Common.Entity
{
    public Guid BarberProfileId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }

    public string? Reason { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberProfiles.BarberProfile BarberProfile { get; private set; } = null!;

    private TimeOff()
    {
    }

    private TimeOff(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? reason)
    {
        BarberProfileId = barberProfileId;
        StartAt = startAt;
        EndAt = endAt;
        Reason = reason;
    }

    /// <summary>
    /// Создаёт период от имени агрегата <c>BarberProfile</c>.
    /// </summary>
    internal static TimeOff Create(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? reason)
        => new(barberProfileId, startAt, endAt, reason);

    /// <summary>
    /// Пересекаются ли два периода. Касание границ (EndAt == другой.StartAt) пересечением не считается.
    /// </summary>
    internal bool Overlaps(DateTimeOffset otherStartAt, DateTimeOffset otherEndAt)
        => StartAt < otherEndAt && otherStartAt < EndAt;
}
