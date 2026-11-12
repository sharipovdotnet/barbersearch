using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;

/// <summary>
/// Команда пометки рабочего дня салона как выходного.
/// </summary>
/// <remarks>
/// Адресация идёт через агрегат-корень: профиль задаёт агрегат, салон — ребёнка,
/// запись расписания — конкретный день внутри салона.
/// </remarks>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="SalonId">Салон, которому принадлежит расписание.</param>
/// <param name="WorkingScheduleId">Строка расписания, которая становится выходным.</param>
/// <param name="Force">
/// Сохранить правку, даже если на этих датах есть активные визиты. Осознанное подтверждение:
/// проверка прав и существования при этом не отключается.
/// </param>
public record SetWorkingScheduleDayOffCommand(
    Guid BarberProfileId,
    Guid SalonId,
    Guid WorkingScheduleId,
    bool Force = false) : ICommand;
