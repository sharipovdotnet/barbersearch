namespace BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;

/// <summary>
/// Один день недельного шаблона в запросе замены.
/// </summary>
/// <remarks>
/// Время — местное время салона. Для выходного дня оно игнорируется и сохраняется
/// как <c>TimeOnly.MinValue</c>.
/// </remarks>
/// <param name="DayOfWeek">День недели.</param>
/// <param name="IsDayOff">Выходной ли это день.</param>
/// <param name="StartTime">Начало рабочего дня.</param>
/// <param name="EndTime">Окончание рабочего дня.</param>
public record ReplaceWorkingScheduleDayRequest(
    DayOfWeek DayOfWeek,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime);
