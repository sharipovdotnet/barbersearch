using System.Globalization;
using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Admin.GetAdminStatistics;

/// <summary>
/// Обработчик админской статистики: проверка периода, плотный ряд и контрактные
/// значения из сырых агрегатов репозитория.
/// </summary>
/// <remarks>
/// <para>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>: решений об авторизации
/// здесь нет. Период включителен по обеим датам и трактуется как UTC-сутки; максимум
/// — 366 дней.
/// </para>
/// <para>
/// «Сейчас» берётся только из <see cref="TimeProvider"/>: от него зависят
/// <c>due</c>/<c>closed</c> и снимок зависших броней, поэтому тест может сдвинуть
/// время без ожидания.
/// </para>
/// <para>
/// Нулевой знаменатель даёт <c>null</c>, а не <c>0</c>: «средний чек» без визитов
/// неизвестен. Точки ряда плотные — обработчик добавляет пропущенные дни и недели
/// нулями, чтобы клиент не додумывал данные.
/// </para>
/// </remarks>
public sealed class GetAdminStatisticsHandler
    : IQueryHandler<AdminStatisticsResponse, GetAdminStatisticsQuery>
{
    private const string DATE_FORMAT = "yyyy-MM-dd";
    private const string CURRENCY = "RUB";
    private const string TIME_ZONE = "UTC";
    private const int DAY_GRANULARITY_MAX_DAYS = 92;

    private readonly IAdminStatisticsRepository _statistics;
    private readonly TimeProvider _timeProvider;

    public GetAdminStatisticsHandler(
        IAdminStatisticsRepository statistics,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _statistics = statistics;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Проверяет период, читает агрегаты и собирает ответ.
    /// </summary>
    public async Task<Result<AdminStatisticsResponse, Failure>> Handle(
        GetAdminStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryParseDay(query.From, out var from)
            || !TryParseDay(query.To, out var to))
        {
            return PeriodInvalid();
        }

        if (from > to)
        {
            return PeriodInvalid();
        }

        var days = to.DayNumber - from.DayNumber + 1;

        if (days > AdminStatisticsFilters.MAX_DAYS)
        {
            return PeriodInvalid();
        }

        var filters = new AdminStatisticsFilters(
            from,
            to,
            query.Compare,
            _timeProvider.GetUtcNow());

        var snapshot = await _statistics.ReadAsync(filters, cancellationToken);

        return Map(query.Compare, filters, snapshot);
    }

    /// <summary>
    /// Собирает контракт: доли, округление, плотный ряд и хвост «Остальные».
    /// </summary>
    private static AdminStatisticsResponse Map(
        bool compare,
        AdminStatisticsFilters filters,
        AdminStatisticsSnapshot snapshot)
    {
        var counters = snapshot.Counters;

        int? PreviousCount(int value) => compare ? value : null;

        var overview = new AdminStatisticsOverviewResponse(
            new AdminStatisticsCountMetricResponse(
                counters.CompletedVisits,
                PreviousCount(counters.PreviousCompletedVisits)),
            new AdminStatisticsMoneyMetricResponse(
                counters.Turnover,
                compare ? counters.PreviousTurnover : null,
                CURRENCY),
            new AdminStatisticsAverageMetricResponse(
                Average(counters.Turnover, counters.CompletedVisits),
                compare
                    ? Average(counters.PreviousTurnover, counters.PreviousCompletedVisits)
                    : null),
            new AdminStatisticsCountMetricResponse(
                counters.NewAccounts,
                PreviousCount(counters.PreviousNewAccounts)),
            new AdminStatisticsNoShowMetricResponse(
                Ratio(counters.NoShowCount, counters.NoShowSample),
                compare
                    ? Ratio(counters.PreviousNoShowCount, counters.PreviousNoShowSample)
                    : null,
                counters.NoShowCount,
                counters.NoShowSample),
            new AdminStatisticsStuckMetricResponse(counters.StuckBookings));

        var series = BuildSeries(filters, snapshot.Series);

        var statuses = snapshot.Statuses;

        var clients = snapshot.Clients;

        var supply = snapshot.Supply;

        var quality = snapshot.Quality;

        var top = snapshot.Top;

        var barberOther = Other(
            counters.CompletedVisits,
            counters.Turnover,
            top.Barbers.Sum(row => row.Visits),
            top.Barbers.Sum(row => row.Turnover));
        var serviceOther = Other(
            counters.CompletedVisits,
            counters.Turnover,
            top.Services.Sum(row => row.Visits),
            top.Services.Sum(row => row.Turnover));
        var cityOther = Other(
            counters.CompletedVisits,
            counters.Turnover,
            top.Cities.Sum(row => row.Visits),
            top.Cities.Sum(row => row.Turnover));

        return new AdminStatisticsResponse(
            new AdminStatisticsPeriodResponse(
                filters.From,
                filters.To,
                filters.Days,
                TIME_ZONE,
                compare
                    ? new AdminStatisticsPeriodRangeResponse(
                        filters.PreviousFrom,
                        filters.PreviousTo)
                    : null),
            filters.Now,
            overview,
            series,
            new AdminStatisticsStatusesResponse(
                statuses.Pending,
                statuses.Confirmed,
                statuses.Completed,
                statuses.Cancelled,
                statuses.NoShow,
                statuses.Due,
                statuses.Closed),
            new AdminStatisticsClientsResponse(
                clients.WithVisit,
                clients.Returning,
                clients.FirstVisit,
                clients.GuestBookings,
                Ratio(clients.GuestBookings, clients.CreatedBookings),
                Ratio(clients.Reviews, counters.CompletedVisits)),
            new AdminStatisticsSupplyResponse(
                supply.ActiveBarbers,
                supply.WorkingBarbers,
                Math.Max(0, supply.ActiveBarbers - supply.WorkingBarbers),
                supply.NewProfiles,
                snapshot.SalonsByKind
                    .Select(kind => new AdminStatisticsSalonKindResponse(
                        kind.Kind,
                        kind.Listed,
                        kind.Unlisted))
                    .ToList()),
            new AdminStatisticsTopResponse(
                new AdminStatisticsBarberTopResponse(
                    top.Barbers
                        .Select(row => new AdminStatisticsBarberTopRowResponse(
                            row.BarberProfileId,
                            row.DisplayName,
                            row.Visits,
                            row.Turnover,
                            Average(row.Turnover, row.Visits) ?? 0m,
                            row.NoShow,
                            Round(row.Rating, 2),
                            row.ReviewsCount))
                        .ToList(),
                    barberOther),
                new AdminStatisticsServiceTopResponse(
                    top.Services
                        .Select(row => new AdminStatisticsServiceTopRowResponse(
                            row.HairServiceId,
                            row.Name,
                            row.Visits,
                            row.Turnover,
                            Average(row.Turnover, row.Visits) ?? 0m))
                        .ToList(),
                    serviceOther),
                new AdminStatisticsCityTopResponse(
                    top.Cities
                        .Select(row => new AdminStatisticsCityTopRowResponse(
                            row.Name,
                            row.Visits,
                            row.Turnover,
                            Average(row.Turnover, row.Visits) ?? 0m,
                            row.ActiveBarbers))
                        .ToList(),
                    cityOther)),
            new AdminStatisticsQualityResponse(
                Round(quality.Rating, 2),
                quality.NewReviews,
                [
                    quality.Rating1,
                    quality.Rating2,
                    quality.Rating3,
                    quality.Rating4,
                    quality.Rating5
                ],
                Ratio(quality.Reviews, quality.NewReviews),
                ToModeration(quality.Requests),
                ToModeration(quality.Salons)));
    }

    /// <summary>
    /// Плотный ряд: каждая дата периода или каждый понедельник от недели начала
    /// до недели конца. Неполные недели остаются в ряду как есть — клиент подписывает
    /// их сам, а сервер не выдумывает полные.
    /// </summary>
    private static AdminStatisticsSeriesResponse BuildSeries(
        AdminStatisticsFilters filters,
        IReadOnlyList<AdminStatisticsSeriesBucket> buckets)
    {
        var byDate = buckets.ToDictionary(bucket => bucket.Date);
        var useDays = filters.Days <= DAY_GRANULARITY_MAX_DAYS;
        var points = new List<AdminStatisticsSeriesPointResponse>();

        if (useDays)
        {
            for (var date = filters.From; date <= filters.To; date = date.AddDays(1))
            {
                var bucket = Find(byDate, date);

                points.Add(new AdminStatisticsSeriesPointResponse(
                    date,
                    bucket.Created,
                    bucket.Completed,
                    bucket.Cancelled,
                    bucket.NoShow));
            }

            return new AdminStatisticsSeriesResponse("day", points);
        }

        var weekStart = StartOfIsoWeek(filters.From);

        while (weekStart <= filters.To)
        {
            var bucket = Find(byDate, weekStart);

            points.Add(new AdminStatisticsSeriesPointResponse(
                weekStart,
                bucket.Created,
                bucket.Completed,
                bucket.Cancelled,
                bucket.NoShow));

            weekStart = weekStart.AddDays(7);
        }

        return new AdminStatisticsSeriesResponse("week", points);
    }

    private static AdminStatisticsSeriesBucket Find(
        Dictionary<DateOnly, AdminStatisticsSeriesBucket> byDate,
        DateOnly date)
        => byDate.TryGetValue(date, out var bucket)
            ? bucket
            : new AdminStatisticsSeriesBucket(date, 0, 0, 0, 0);

    /// <summary>Понедельник ISO-недели, в которую попадает дата.</summary>
    private static DateOnly StartOfIsoWeek(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Хвост топа: итог периода минус строки. Отрицательное значение обрезается
    /// до нуля — это защита от гонки двух чтений, а не источник чисел.
    /// </summary>
    private static AdminStatisticsTopOtherResponse Other(
        int totalVisits,
        decimal totalTurnover,
        int topVisits,
        decimal topTurnover)
        => new(
            Math.Max(0, totalVisits - topVisits),
            Math.Max(0m, totalTurnover - topTurnover));

    private static AdminStatisticsModerationResponse ToModeration(
        AdminStatisticsModeration moderation)
        => new(
            moderation.Submitted,
            moderation.Approved,
            moderation.Rejected,
            Ratio(
                moderation.Approved,
                moderation.Approved + moderation.Rejected),
            Round(moderation.MedianDecisionHours, 1));

    /// <summary>Доля с честным <c>null</c> при нулевом знаменателе, 4 знака.</summary>
    private static decimal? Ratio(int numerator, int denominator)
        => denominator == 0
            ? null
            : decimal.Round(
                (decimal)numerator / denominator,
                4,
                MidpointRounding.AwayFromZero);

    /// <summary>Средний чек: <c>null</c> при нулевом числе визитов.</summary>
    private static decimal? Average(decimal turnover, int count)
        => count == 0
            ? null
            : decimal.Round(
                turnover / count,
                2,
                MidpointRounding.AwayFromZero);

    private static decimal? Round(decimal? value, int digits)
        => value is { } number
            ? decimal.Round(number, digits, MidpointRounding.AwayFromZero)
            : null;

    private static decimal? Round(double? value, int digits)
        => value is { } number
            ? decimal.Round((decimal)number, digits, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>
    /// Разбирает дату периода: пустая строка, другой формат и лишние символы — ошибка.
    /// </summary>
    private static bool TryParseDay(string? value, out DateOnly day)
    {
        day = default;

        return !string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(
                value.Trim(),
                DATE_FORMAT,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out day);
    }

    private static Failure PeriodInvalid()
        => new(Error.Validation(
            ApiErrorCodes.AdminStatistics.PeriodInvalid,
            "Period must use the yyyy-MM-dd format, the start must not be after the end "
            + $"and it must not exceed {AdminStatisticsFilters.MAX_DAYS} days.",
            "from"));
}
