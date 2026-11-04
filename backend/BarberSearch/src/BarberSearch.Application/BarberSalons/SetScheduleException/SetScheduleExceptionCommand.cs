using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberSalons.SetScheduleException;

/// <summary>
/// Команда постановки исключения на конкретную дату.
/// </summary>
/// <param name="BarberProfileId">Профиль барбера — корень агрегата.</param>
/// <param name="SalonId">Салон, к которому относится дата.</param>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="IsDayOff">Выходной ли это день.</param>
/// <param name="StartTime">Начало своих часов; игнорируется для выходного.</param>
/// <param name="EndTime">Окончание своих часов; игнорируется для выходного.</param>
/// <param name="Reason">Пометка барбера или <c>null</c>.</param>
public record SetScheduleExceptionCommand(
    Guid BarberProfileId,
    Guid SalonId,
    DateOnly Date,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason) : ICommand;
