using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberSalons.AddBarberSalonWorkingSchedule;

/// <summary>
/// Команда добавления рабочего дня в расписание салона барбера.
/// </summary>
/// <remarks>
/// Адресация идёт через агрегат-корень: идентификатор профиля задаёт агрегат,
/// идентификатор салона — конкретного ребёнка внутри его коллекции.
/// </remarks>
public record AddBarberSalonWorkingScheduleCommand(
    Guid BarberProfileId,
    Guid SalonId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime) : ICommand;
