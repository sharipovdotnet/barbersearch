using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Admin;

namespace BarberSearch.Application.Admin.GetAdminStatistics;

/// <summary>
/// Запрос админской статистики за период.
/// </summary>
/// <remarks>
/// Даты приходят строками, а не <see cref="DateOnly"/>: так обработчик сам отвечает
/// кодом <c>admin.statistics.period</c> и на отсутствующую дату, и на кривой формат,
/// вместо автоматического <c>400</c> привязки модели с чужим текстом. Формат —
/// <c>yyyy-MM-dd</c>, обе даты обязательны.
/// </remarks>
/// <param name="From">Первый день периода включительно.</param>
/// <param name="To">Последний день периода включительно.</param>
/// <param name="Compare">Сравнивать ли с предыдущим периодом той же длины.</param>
public sealed record GetAdminStatisticsQuery(
    string? From,
    string? To,
    bool Compare) : IQuery<AdminStatisticsResponse>;
