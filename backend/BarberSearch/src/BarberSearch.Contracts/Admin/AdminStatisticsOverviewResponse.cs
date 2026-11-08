namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Плитки обзора: счётчики, деньги и доли с прошлым периодом.
/// </summary>
/// <param name="CompletedVisits">Завершённые визиты по <c>start_at</c>.</param>
/// <param name="Turnover">Оборот по завершённым визитам; это не выручка.</param>
/// <param name="AverageCheck">Оборот, делённый на завершённые визиты.</param>
/// <param name="NewAccounts">Аккаунты, созданные в периоде.</param>
/// <param name="NoShowRate">Неявки среди завершённых и неявившихся.</param>
/// <param name="StuckBookings">Зависшие брони: активные со стартом в прошлом.</param>
public record AdminStatisticsOverviewResponse(
    AdminStatisticsCountMetricResponse CompletedVisits,
    AdminStatisticsMoneyMetricResponse Turnover,
    AdminStatisticsAverageMetricResponse AverageCheck,
    AdminStatisticsCountMetricResponse NewAccounts,
    AdminStatisticsNoShowMetricResponse NoShowRate,
    AdminStatisticsStuckMetricResponse StuckBookings);

/// <summary>
/// Целочисленный счётчик с значением прошлого периода; <c>previous</c> = <c>null</c>
/// при <c>compare=false</c>.
/// </summary>
/// <param name="Value">Значение текущего периода.</param>
/// <param name="Previous">Значение предыдущего периода или <c>null</c>.</param>
public record AdminStatisticsCountMetricResponse(int Value, int? Previous);

/// <summary>
/// Денежное значение периода и валюта.
/// </summary>
/// <param name="Value">Сумма в валюте <paramref name="Currency"/>.</param>
/// <param name="Previous">Сумма предыдущего периода или <c>null</c>.</param>
/// <param name="Currency">Валюта; в домене валюты нет, поэтому контракт фиксирует <c>RUB</c>.</param>
public record AdminStatisticsMoneyMetricResponse(
    decimal Value,
    decimal? Previous,
    string Currency);

/// <summary>
/// Среднее значение (средний чек) с прошлым периодом.
/// </summary>
/// <param name="Value">Значение текущего периода или <c>null</c>, если визитов 0.</param>
/// <param name="Previous">Значение предыдущего периода или <c>null</c>.</param>
public record AdminStatisticsAverageMetricResponse(decimal? Value, decimal? Previous);

/// <summary>
/// Доля неявок с знаменателем: процент без базы не означает ничего.
/// </summary>
/// <param name="Value">Доля <c>0..1</c> или <c>null</c> при нулевом знаменателе.</param>
/// <param name="Previous">Доля предыдущего периода или <c>null</c>.</param>
/// <param name="Count">Число неявок в текущем периоде — для честного показа малых чисел.</param>
/// <param name="Sample">Знаменатель: завершённые плюс неявившиеся визиты периода.</param>
public record AdminStatisticsNoShowMetricResponse(
    decimal? Value,
    decimal? Previous,
    int Count,
    int Sample);

/// <summary>
/// Снимок зависших броней: это не период, поэтому прошлого значения нет.
/// </summary>
/// <param name="Value">Число активных броней со стартом в прошлом.</param>
public record AdminStatisticsStuckMetricResponse(int Value);
