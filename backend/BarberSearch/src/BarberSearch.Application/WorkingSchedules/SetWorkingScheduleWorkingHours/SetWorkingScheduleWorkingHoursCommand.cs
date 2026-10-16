using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleWorkingHours;

/// <summary>
/// Команда изменения рабочих часов в расписании салона барбера.
/// </summary>
/// <remarks>
/// Установка часов снимает признак выходного дня — это делает доменный метод.
/// </remarks>
public record SetWorkingScheduleWorkingHoursCommand(
    Guid BarberProfileId,
    Guid SalonId,
    Guid WorkingScheduleId,
    TimeOnly StartTime,
    TimeOnly EndTime) : ICommand;
