namespace BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;

/// <summary>
/// Данные запроса на добавление нерабочего периода (отпуска) барбера.
/// </summary>
/// <remarks>
/// Времена передаются в UTC; порядок границ и пересечение с другими отпусками проверяет домен.
/// </remarks>
public record AddBarberProfileTimeOffRequest(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason);
