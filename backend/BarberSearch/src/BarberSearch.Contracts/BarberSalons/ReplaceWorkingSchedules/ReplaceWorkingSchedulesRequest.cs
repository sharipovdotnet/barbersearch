namespace BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;

/// <summary>
/// Полная замена недельного шаблона пары (барбер, салон).
/// </summary>
/// <remarks>
/// Дней должно быть ровно семь, по одному на каждый день недели. Повтор дня или
/// неполный набор отклоняет домен; клиент не собирает расписание по одной строке.
/// </remarks>
/// <param name="Days">Семь дней недели.</param>
public record ReplaceWorkingSchedulesRequest(
    IReadOnlyList<ReplaceWorkingScheduleDayRequest> Days);
