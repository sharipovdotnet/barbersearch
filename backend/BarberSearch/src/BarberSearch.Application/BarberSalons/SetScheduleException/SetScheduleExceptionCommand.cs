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
/// <param name="Force">
/// Сохранить правку, даже если на этой дате есть активные визиты. Осознанное подтверждение:
/// проверка прав и существования при этом не отключается. Явная дата проверяется и за
/// горизонтом повторяющихся дней.
/// </param>
public record SetScheduleExceptionCommand(
    Guid BarberProfileId,
    Guid SalonId,
    DateOnly Date,
    bool IsDayOff,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason,
    bool Force = false) : ICommand;
