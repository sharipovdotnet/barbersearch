namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Плотный ряд записей и визитов: каждая дата или неделя периода присутствует,
/// включая нулевые.
/// </summary>
/// <param name="Granularity">
/// <c>day</c> при периоде до 92 дней включительно, иначе <c>week</c> — ISO-неделя
/// с понедельника. Первый и последний недельный бакет могут быть неполными.
/// </param>
/// <param name="Points">Точки в хронологическом порядке без пропусков.</param>
public record AdminStatisticsSeriesResponse(
    string Granularity,
    IReadOnlyList<AdminStatisticsSeriesPointResponse> Points);

/// <summary>
/// Одна точка ряда. <c>created</c> считается по <c>created_at</c>,
/// остальные — по <c>start_at</c>.
/// </summary>
/// <param name="Date">День или понедельник ISO-недели.</param>
/// <param name="Created">Созданные записи.</param>
/// <param name="Completed">Завершённые визиты.</param>
/// <param name="Cancelled">Отменённые визиты.</param>
/// <param name="NoShow">Неявившиеся визиты.</param>
public record AdminStatisticsSeriesPointResponse(
    DateOnly Date,
    int Created,
    int Completed,
    int Cancelled,
    int NoShow);
