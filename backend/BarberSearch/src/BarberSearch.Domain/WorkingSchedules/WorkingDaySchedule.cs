namespace BarberSearch.Domain.WorkingSchedules;

/// <summary>
/// Один день недельного шаблона в команде замены расписания.
/// </summary>
/// <remarks>
/// Для выходного дня <see cref="StartTime"/> и <see cref="EndTime"/> равны
/// <see cref="TimeOnly.MinValue"/>: агрегат сам проверяет границы рабочих дней,
/// поэтому команда обязана передать все семь дней.
/// </remarks>
/// <param name="DayOfWeek">День недели.</param>
/// <param name="IsDayOff">Выходной ли это день.</param>
/// <param name="StartTime">Начало рабочего дня по локальному времени салона.</param>
/// <param name="EndTime">Окончание рабочего дня по локальному времени салона.</param>
public sealed record WorkingDaySchedule(
    DayOfWeek DayOfWeek,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime);
