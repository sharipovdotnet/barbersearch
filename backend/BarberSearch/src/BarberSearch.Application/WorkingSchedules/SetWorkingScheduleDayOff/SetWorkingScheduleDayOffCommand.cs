using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;

/// <summary>
/// Команда пометки рабочего дня салона как выходного.
/// </summary>
/// <remarks>
/// Адресация идёт через агрегат-корень: профиль задаёт агрегат, салон — ребёнка,
/// запись расписания — конкретный день внутри салона.
/// </remarks>
public record SetWorkingScheduleDayOffCommand(
    Guid BarberProfileId,
    Guid SalonId,
    Guid WorkingScheduleId) : ICommand;
