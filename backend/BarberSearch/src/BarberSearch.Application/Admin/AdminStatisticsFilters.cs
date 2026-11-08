namespace BarberSearch.Application.Admin;

/// <summary>
/// Проверенные границы периода статистики и момент расчёта.
/// </summary>
/// <remarks>
/// Период включителен по обеим датам и трактуется как UTC-сутки: запросы используют
/// <c>&gt;= FromInclusive</c> и <c>&lt; ToExclusive</c>. Предыдущий период той же длины
/// считается всегда: обработчик прячет его в ответе при <c>compare=false</c>, а SQL
/// остаётся одним и тем же.
/// </remarks>
/// <param name="From">Первый день периода включительно.</param>
/// <param name="To">Последний день периода включительно.</param>
/// <param name="Compare">Нужно ли сравнивать с предыдущим периодом.</param>
/// <param name="Now">Момент расчёта из <see cref="TimeProvider"/>; не системные часы.</param>
public sealed record AdminStatisticsFilters(
    DateOnly From,
    DateOnly To,
    bool Compare,
    DateTimeOffset Now)
{
    /// <summary>
    /// Предел длины периода: год включительно. Больше — ошибка периода, а не молчаливое
    /// усечение: подмена запрошенного диапазона исказила бы числа.
    /// </summary>
    public const int MAX_DAYS = 366;

    /// <summary>Число дней периода, включая обе границы.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>Начало периода: <c>00:00Z</c> первого дня.</summary>
    public DateTimeOffset FromInclusive => StartOfUtcDay(From);

    /// <summary>Конец периода: <c>00:00Z</c> дня после последнего.</summary>
    public DateTimeOffset ToExclusive => StartOfUtcDay(To.AddDays(1));

    /// <summary>Последний день предыдущего периода той же длины.</summary>
    public DateOnly PreviousTo => From.AddDays(-1);

    /// <summary>Первый день предыдущего периода той же длины.</summary>
    public DateOnly PreviousFrom => PreviousTo.AddDays(-(Days - 1));

    /// <summary>Начало предыдущего периода: <c>00:00Z</c>.</summary>
    public DateTimeOffset PreviousFromInclusive => StartOfUtcDay(PreviousFrom);

    /// <summary>Конец предыдущего периода: <c>00:00Z</c> дня после последнего.</summary>
    public DateTimeOffset PreviousToExclusive => StartOfUtcDay(From);

    private static DateTimeOffset StartOfUtcDay(DateOnly day)
        => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
