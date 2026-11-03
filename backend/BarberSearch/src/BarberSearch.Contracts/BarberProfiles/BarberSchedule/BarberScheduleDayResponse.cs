namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Один день недельного шаблона.
/// </summary>
/// <remarks>
/// Для выходного дня <see cref="StartTime"/> и <see cref="EndTime"/> равны
/// <c>TimeOnly.MinValue</c>; смотреть на них нужно только при <see cref="IsDayOff"/> = <c>false</c>.
/// </remarks>
public record BarberScheduleDayResponse(
    DayOfWeek DayOfWeek,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime);
