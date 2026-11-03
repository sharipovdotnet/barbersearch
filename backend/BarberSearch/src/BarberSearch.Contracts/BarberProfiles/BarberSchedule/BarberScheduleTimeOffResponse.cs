namespace BarberSearch.Contracts.BarberProfiles.BarberSchedule;

/// <summary>
/// Отпуск или другой нерабочий период профиля.
/// </summary>
/// <param name="Id">Идентификатор периода.</param>
/// <param name="StartAt">Начало периода в UTC.</param>
/// <param name="EndAt">Окончание периода в UTC.</param>
/// <param name="Reason">Пометка барбера или <c>null</c>.</param>
public record BarberScheduleTimeOffResponse(
    Guid Id,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason);
