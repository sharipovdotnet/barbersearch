namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Исключение из недельного шаблона на конкретную дату.
/// </summary>
/// <remarks>
/// Для выходного дня время равно <c>TimeOnly.MinValue</c> и не показывается.
/// </remarks>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="IsDayOff">Выходной ли это день.</param>
/// <param name="StartTime">Начало своих часов; <c>MinValue</c> для выходного.</param>
/// <param name="EndTime">Окончание своих часов; <c>MinValue</c> для выходного.</param>
/// <param name="Reason">Пометка барбера или <c>null</c>.</param>
public record BarberScheduleExceptionResponse(
    DateOnly Date,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason);
