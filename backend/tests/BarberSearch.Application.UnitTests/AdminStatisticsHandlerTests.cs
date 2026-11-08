using BarberSearch.Application.Admin;
using BarberSearch.Application.Admin.GetAdminStatistics;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Обработчик админской статистики: границы периода, предыдущий период, честные
/// <c>null</c> при нулевых знаменателях, плотный ряд, хвост «Остальные» и округление.
/// </summary>
/// <remarks>
/// SQL-агрегаты проверяются интеграционными тестами: здесь репозиторий подменён
/// сырыми числами, поэтому тест видит только решения обработчика и не зависит от базы.
/// </remarks>
public class AdminStatisticsHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private static readonly int[] ExpectedDistribution = [0, 1, 2, 5, 30];

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "2026-11-08")]
    [InlineData("2026-13-01", "2026-11-08")]
    [InlineData("2026-11-08", "2026-11-01")]
    [InlineData("2026-01-01", "2027-01-02")]
    public async Task Handle_RejectsMissingMalformedReversedOrTooLongPeriod(
        string? from,
        string? to)
    {
        var repository = new FakeAdminStatisticsRepository();

        var result = await HandleAsync(repository, from, to);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.AdminStatistics.PeriodInvalid,
            result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Fact]
    public async Task Handle_PassesUtcBoundsAndPreviousPeriodToTheRepository()
    {
        var repository = new FakeAdminStatisticsRepository();

        var result = await HandleAsync(
            repository,
            "2026-10-10",
            "2026-11-08",
            compare: true);

        Assert.True(result.IsSuccess);

        var filters = Assert.IsType<AdminStatisticsFilters>(repository.LastFilters);
        Assert.Equal(new DateOnly(2026, 10, 10), filters.From);
        Assert.Equal(new DateOnly(2026, 11, 8), filters.To);
        Assert.Equal(30, filters.Days);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), filters.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), filters.ToExclusive);
        Assert.Equal(new DateOnly(2026, 9, 10), filters.PreviousFrom);
        Assert.Equal(new DateOnly(2026, 10, 9), filters.PreviousTo);
        Assert.Equal(Now, filters.Now);

        Assert.Equal("UTC", result.Value.Period.TimeZone);
        Assert.Equal(30, result.Value.Period.Days);
        Assert.Equal(
            new AdminStatisticsPeriodRangeResponse(
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 10, 9)),
            result.Value.Period.Previous);
        Assert.Equal(Now, result.Value.GeneratedAt);
    }

    [Fact]
    public async Task Handle_WithoutCompare_HidesPreviousValues()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Counters = new AdminStatisticsCounters(
                    CompletedVisits: 10,
                    Turnover: 1000m,
                    NoShowCount: 1,
                    NoShowSample: 11,
                    PreviousCompletedVisits: 8,
                    PreviousTurnover: 900m,
                    PreviousNoShowCount: 2,
                    PreviousNoShowSample: 10,
                    StuckBookings: 3,
                    NewAccounts: 4,
                    PreviousNewAccounts: 5)
            }
        };

        var result = await HandleAsync(repository, "2026-10-10", "2026-11-08");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Period.Previous);
        Assert.Null(result.Value.Overview.CompletedVisits.Previous);
        Assert.Null(result.Value.Overview.Turnover.Previous);
        Assert.Null(result.Value.Overview.AverageCheck.Previous);
        Assert.Null(result.Value.Overview.NewAccounts.Previous);
        Assert.Null(result.Value.Overview.NoShowRate.Previous);

        Assert.Equal(10, result.Value.Overview.CompletedVisits.Value);
        Assert.Equal(1000m, result.Value.Overview.Turnover.Value);
        Assert.Equal("RUB", result.Value.Overview.Turnover.Currency);
        Assert.Equal(100m, result.Value.Overview.AverageCheck.Value);
        Assert.Equal(0.0909m, result.Value.Overview.NoShowRate.Value);
        Assert.Equal(1, result.Value.Overview.NoShowRate.Count);
        Assert.Equal(11, result.Value.Overview.NoShowRate.Sample);
        Assert.Equal(3, result.Value.Overview.StuckBookings.Value);
    }

    [Fact]
    public async Task Handle_WithCompare_ReturnsPreviousValues()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Counters = new AdminStatisticsCounters(10, 1000m, 1, 11, 8, 800m, 2, 10, 0, 4, 5)
            }
        };

        var result = await HandleAsync(
            repository,
            "2026-10-10",
            "2026-11-08",
            compare: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Value.Overview.CompletedVisits.Previous);
        Assert.Equal(800m, result.Value.Overview.Turnover.Previous);
        Assert.Equal(100m, result.Value.Overview.AverageCheck.Previous);
        Assert.Equal(0.2m, result.Value.Overview.NoShowRate.Previous);
        Assert.Equal(5, result.Value.Overview.NewAccounts.Previous);
    }

    [Fact]
    public async Task Handle_WithZeroDenominators_ReturnsNullInsteadOfZero()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Clients = new AdminStatisticsClients(0, 0, 0, 0, 0, 0),
                Quality = Quality() with
                {
                    Rating = null,
                    Reviews = 0,
                    NewReviews = 0,
                    Requests = new AdminStatisticsModeration(0, 0, 0, null),
                    Salons = new AdminStatisticsModeration(0, 0, 0, null)
                }
            }
        };

        var result = await HandleAsync(repository, "2026-10-10", "2026-11-08");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Overview.AverageCheck.Value);
        Assert.Null(result.Value.Overview.NoShowRate.Value);
        Assert.Null(result.Value.Clients.GuestShare);
        Assert.Null(result.Value.Clients.ReviewRate);
        Assert.Null(result.Value.Quality.Rating);
        Assert.Null(result.Value.Quality.PublishedShare);
        Assert.Null(result.Value.Quality.Requests.ApprovalRate);
        Assert.Null(result.Value.Quality.Requests.MedianDecisionHours);
        Assert.Null(result.Value.Quality.Salons.ApprovalRate);

        // Ноль остаётся нулём, а не «неизвестно»: это посчитанное значение.
        Assert.Equal(0, result.Value.Overview.CompletedVisits.Value);
        Assert.Equal(0m, result.Value.Overview.Turnover.Value);
    }

    [Fact]
    public async Task Handle_BuildsDenseDailySeriesWithZeros()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Series =
                [
                    new AdminStatisticsSeriesBucket(
                        new DateOnly(2026, 11, 2), 3, 2, 1, 0)
                ]
            }
        };

        var result = await HandleAsync(repository, "2026-11-01", "2026-11-07");

        Assert.True(result.IsSuccess);
        Assert.Equal("day", result.Value.Series.Granularity);
        Assert.Equal(7, result.Value.Series.Points.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), result.Value.Series.Points[0].Date);
        Assert.Equal(0, result.Value.Series.Points[0].Created);
        Assert.Equal(new DateOnly(2026, 11, 2), result.Value.Series.Points[1].Date);
        Assert.Equal(3, result.Value.Series.Points[1].Created);
        Assert.Equal(2, result.Value.Series.Points[1].Completed);
        Assert.Equal(1, result.Value.Series.Points[1].Cancelled);
        Assert.Equal(new DateOnly(2026, 11, 7), result.Value.Series.Points[^1].Date);
    }

    [Fact]
    public async Task Handle_SwitchesToIsoWeeksAfterNinetyTwoDays()
    {
        var repository = new FakeAdminStatisticsRepository();
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 4, 15);

        var result = await HandleAsync(repository, "2026-01-01", "2026-04-15");

        Assert.True(result.IsSuccess);
        Assert.Equal("week", result.Value.Series.Granularity);

        var points = result.Value.Series.Points;
        var expectedWeeks = (int)Math.Ceiling(
            (to.DayNumber - StartOfIsoWeek(from).DayNumber + 1) / 7.0);

        Assert.Equal(expectedWeeks, points.Count);
        Assert.Equal(StartOfIsoWeek(from), points[0].Date);
        Assert.Equal(DayOfWeek.Monday, points[0].Date.DayOfWeek);
        Assert.All(points, point => Assert.Equal(DayOfWeek.Monday, point.Date.DayOfWeek));

        for (var index = 1; index < points.Count; index += 1)
        {
            Assert.Equal(7, points[index].Date.DayNumber - points[index - 1].Date.DayNumber);
        }
    }

    [Fact]
    public async Task Handle_TopOtherIsTheRemainderOfThePeriodTotal()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Counters = new AdminStatisticsCounters(100, 1000m, 0, 100, 0, 0m, 0, 0, 0, 0, 0),
                Top = new AdminStatisticsTop(
                    [
                        Barber(20, 200m, noShow: 2),
                        Barber(10, 100m, noShow: 1)
                    ],
                    [
                        new AdminStatisticsServiceRow(Guid.CreateVersion7(), "Fade", 5, 50m)
                    ],
                    [
                        new AdminStatisticsCityRow("Москва", 30, 300m, 7)
                    ])
            }
        };

        var result = await HandleAsync(repository, "2026-10-10", "2026-11-08");

        Assert.True(result.IsSuccess);

        var top = result.Value.Top;
        Assert.Equal(2, top.Barbers.Rows.Count);
        Assert.Equal(30, top.Barbers.Rows.Sum(row => row.Visits));
        Assert.Equal(300m, top.Barbers.Rows.Sum(row => row.Turnover));
        Assert.Equal(70, top.Barbers.Other.Visits);
        Assert.Equal(700m, top.Barbers.Other.Turnover);
        Assert.Equal(100, top.Barbers.Rows.Sum(row => row.Visits) + top.Barbers.Other.Visits);

        Assert.Equal(95, top.Services.Other.Visits);
        Assert.Equal(950m, top.Services.Other.Turnover);
        Assert.Equal(70, top.Cities.Other.Visits);
        Assert.Equal(700m, top.Cities.Other.Turnover);

        Assert.Equal(10m, top.Barbers.Rows[0].AverageCheck);
        Assert.Equal(2, top.Barbers.Rows[0].NoShow);
    }

    [Fact]
    public async Task Handle_RoundsRatesMeansAndMedians()
    {
        var repository = new FakeAdminStatisticsRepository
        {
            Snapshot = Snapshot() with
            {
                Counters = new AdminStatisticsCounters(3, 100m, 0, 3, 0, 0m, 0, 0, 0, 0, 0),
                Clients = new AdminStatisticsClients(3, 1, 2, 1, 4, 1),
                Supply = new AdminStatisticsSupply(5, 7, 2),
                Quality = new AdminStatisticsQuality(
                    Rating: 4.7234m,
                    Reviews: 3,
                    Rating1: 0,
                    Rating2: 1,
                    Rating3: 2,
                    Rating4: 5,
                    Rating5: 30,
                    NewReviews: 8,
                    Requests: new AdminStatisticsModeration(4, 2, 1, 26.55),
                    Salons: new AdminStatisticsModeration(3, 1, 1, 48.04)),
                SalonsByKind =
                [
                    new AdminStatisticsSalonKind("Salon", 30, 0),
                    new AdminStatisticsSalonKind("Home", 0, 4)
                ]
            }
        };

        var result = await HandleAsync(repository, "2026-10-10", "2026-11-08");

        Assert.True(result.IsSuccess);
        Assert.Equal(33.33m, result.Value.Overview.AverageCheck.Value);
        Assert.Equal(4.72m, result.Value.Quality.Rating);
        Assert.Equal(ExpectedDistribution, result.Value.Quality.Distribution);

        // 1/4 гостевых записей и 1/3 визитов с отзывом — четыре знака.
        Assert.Equal(0.25m, result.Value.Clients.GuestShare);
        Assert.Equal(0.3333m, result.Value.Clients.ReviewRate);

        // Одобрения считаются от решённых: 2 / (2 + 1).
        Assert.Equal(0.6667m, result.Value.Quality.Requests.ApprovalRate);
        Assert.Equal(26.6m, result.Value.Quality.Requests.MedianDecisionHours);
        Assert.Equal(0.5m, result.Value.Quality.Salons.ApprovalRate);
        Assert.Equal(48m, result.Value.Quality.Salons.MedianDecisionHours);

        // Работавших больше активных — «без визитов» отрицательным не бывает.
        Assert.Equal(0, result.Value.Supply.IdleBarbers);
    }

    [Fact]
    public async Task Handle_EmptyPeriod_ReturnsEmptyTopsAndZeroSeries()
    {
        var repository = new FakeAdminStatisticsRepository();

        var result = await HandleAsync(repository, "2026-10-10", "2026-11-08");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Top.Barbers.Rows);
        Assert.Empty(result.Value.Top.Services.Rows);
        Assert.Empty(result.Value.Top.Cities.Rows);
        Assert.Equal(0, result.Value.Top.Barbers.Other.Visits);
        Assert.Equal(0, result.Value.Top.Services.Other.Visits);
        Assert.Equal(0, result.Value.Top.Cities.Other.Visits);
        Assert.Equal(30, result.Value.Series.Points.Count);
        Assert.All(result.Value.Series.Points, point => Assert.Equal(0, point.Created));
        Assert.Empty(result.Value.Supply.SalonsByKind);
        Assert.Equal("RUB", result.Value.Overview.Turnover.Currency);
    }

    private static Task<Result<AdminStatisticsResponse, Failure>> HandleAsync(
        FakeAdminStatisticsRepository repository,
        string? from,
        string? to,
        bool compare = false)
    {
        var handler = new GetAdminStatisticsHandler(
            repository,
            new FakeTimeProvider(Now));

        return handler.Handle(
            new GetAdminStatisticsQuery(from, to, compare),
            CancellationToken.None);
    }

    private static AdminStatisticsBarberRow Barber(int visits, decimal turnover, int noShow)
        => new(
            Guid.CreateVersion7(),
            $"Барбер {visits}",
            visits,
            turnover,
            noShow,
            4.8m,
            12);

    private static AdminStatisticsQuality Quality()
        => new(
            Rating: 4.5m,
            Reviews: 2,
            Rating1: 0,
            Rating2: 0,
            Rating3: 0,
            Rating4: 1,
            Rating5: 1,
            NewReviews: 2,
            Requests: new AdminStatisticsModeration(0, 0, 0, null),
            Salons: new AdminStatisticsModeration(0, 0, 0, null));

    private static AdminStatisticsSnapshot Snapshot()
        => new(
            new AdminStatisticsCounters(0, 0m, 0, 0, 0, 0m, 0, 0, 0, 0, 0),
            new AdminStatisticsStatuses(0, 0, 0, 0, 0, 0, 0),
            [],
            new AdminStatisticsClients(0, 0, 0, 0, 0, 0),
            new AdminStatisticsSupply(0, 0, 0),
            [],
            Quality(),
            new AdminStatisticsTop([], [], []));

    private static DateOnly StartOfIsoWeek(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private sealed class FakeAdminStatisticsRepository : IAdminStatisticsRepository
    {
        public AdminStatisticsSnapshot Snapshot { get; set; } = AdminStatisticsHandlerTests.Snapshot();

        public AdminStatisticsFilters? LastFilters { get; private set; }

        public Task<AdminStatisticsSnapshot> ReadAsync(
            AdminStatisticsFilters filters,
            CancellationToken cancellationToken)
        {
            LastFilters = filters;

            return Task.FromResult(Snapshot);
        }
    }
}
