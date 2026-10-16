using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberSalons.RemoveBarberSalonWorkingSchedule;

/// <summary>
/// Команда удаления записи расписания из салона барбера.
/// </summary>
/// <remarks>
/// Удаление идёт через агрегат-корень: профиль задаёт агрегат, салон — ребёнка,
/// запись расписания — конкретный рабочий день внутри салона.
/// </remarks>
public record RemoveBarberSalonWorkingScheduleCommand(
    Guid BarberProfileId,
    Guid SalonId,
    Guid WorkingScheduleId) : ICommand;
