namespace BarberSearch.Contracts.BarberSalons.SetScheduleException;

/// <summary>
/// Исключение на конкретную дату: выходной или свои часы.
/// </summary>
/// <remarks>
/// Дата приходит из маршрута, а не из тела: она — часть адреса ресурса.
/// </remarks>
/// <param name="IsDayOff">Выходной ли это день.</param>
/// <param name="StartTime">Начало своих часов; игнорируется для выходного.</param>
/// <param name="EndTime">Окончание своих часов; игнорируется для выходного.</param>
/// <param name="Reason">Пометка барбера или <c>null</c>.</param>
public record SetScheduleExceptionRequest(
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason);
