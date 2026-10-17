namespace BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Ответ с одной записью расписания салона барбера.
/// </summary>
/// <remarks>
/// Для выходного дня <see cref="StartTime"/> и <see cref="EndTime"/> равны
/// <c>TimeOnly.MinValue</c>: смотреть на них нужно только при <see cref="IsDayOff"/> = <c>false</c>.
/// </remarks>
public record WorkingScheduleResponse(
    Guid Id,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsDayOff);
