using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberSalons.AddBarberSalonDayOff;

/// <summary>
/// Команда добавления нерабочего дня в расписание салона барбера.
/// </summary>
/// <remarks>
/// Время для нерабочего дня не задаётся: запись создаётся с признаком «выходной».
/// </remarks>
public record AddBarberSalonDayOffCommand(
    Guid BarberProfileId,
    Guid SalonId,
    DayOfWeek DayOfWeek) : ICommand;
