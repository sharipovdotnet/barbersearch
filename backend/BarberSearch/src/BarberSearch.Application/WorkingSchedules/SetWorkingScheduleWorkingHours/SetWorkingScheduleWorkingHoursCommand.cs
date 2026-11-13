using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleWorkingHours;

/// <summary>
/// Команда изменения рабочих часов в расписании салона барбера.
/// </summary>
/// <remarks>
/// Установка часов снимает признак выходного дня — это делает доменный метод.
/// </remarks>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="SalonId">Салон, которому принадлежит расписание.</param>
/// <param name="WorkingScheduleId">Изменяемая строка расписания.</param>
/// <param name="StartTime">Новое начало рабочего дня.</param>
/// <param name="EndTime">Новое окончание рабочего дня.</param>
/// <param name="Force">
/// Сохранить правку, даже если активные визиты окажутся вне новых часов. Осознанное
/// подтверждение: проверка прав и существования при этом не отключается.
/// </param>
public record SetWorkingScheduleWorkingHoursCommand(
    Guid BarberProfileId,
    Guid SalonId,
    Guid WorkingScheduleId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool Force = false) : ICommand;
