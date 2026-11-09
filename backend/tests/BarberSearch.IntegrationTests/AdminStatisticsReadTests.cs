using System.Net;
using System.Text.Json;
using BarberSearch.Contracts.Admin;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админская статистика (<c>GET api/admin/statistics</c>) на реальной PostgreSQL:
/// доступ, границы UTC-суток, предыдущий период, доли, топы с хвостом «Остальные»,
/// плотный ряд и отсутствие PII в теле.
/// </summary>
/// <remarks>
/// <para>
/// Часы подменяются на момент запуска прогона: <c>due</c>, <c>closed</c> и снимок зависших
/// броней зависят от «сейчас», а не от системного времени. Период — январь 2020,
/// поэтому аккаунты, созданные сидом сейчас, в «новые аккаунты» не попадают.
/// </para>
/// <para>
/// Конкретная дата не фиксируется: <see cref="BarberSearch.Web.Auth.JwtTokenIssuer"/> берёт
/// «сейчас» из <see cref="TimeProvider"/>, а валидация bearer-токена — из системных часов.
/// Замороженное прошлое делало выданный access-токен просроченным, и каждый аутентифицированный
/// запрос отвечал <c>401</c>.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminStatisticsReadTests
{
    private const string ENDPOINT = "api/admin/statistics";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly int[] ExpectedDistribution = [0, 0, 0, 0, 1];

    private readonly PostgresFixture _database;

    public AdminStatisticsReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Anonymous_gets_401_non_admin_gets_403_and_admin_gets_200()
    {
        await using var app = await StartAsync();

        using (var anonymous = await app.Client.GetAsync($"{ENDPOINT}?from=2020-01-01&to=2020-01-07"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var customer = await app.CreateUserAsync(
            $"stats-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using (var forbidden = await app.Client.GetAsync($"{ENDPOINT}?from=2020-01-01&to=2020-01-07"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var admin = await app.CreateUserAsync(
            $"stats-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync($"{ENDPOINT}?from=2020-01-01&to=2020-01-07");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statistics = await ReadAsync(response);
        Assert.Equal(new DateOnly(2020, 1, 1), statistics.Period.From);
        Assert.Equal(new DateOnly(2020, 1, 7), statistics.Period.To);
        Assert.Equal(7, statistics.Period.Days);
        Assert.Null(statistics.Period.Previous);
    }

    [Theory(Timeout = 120_000)]
    [InlineData("")]
    [InlineData("?from=2020-01-01")]
    [InlineData("?from=2020-13-01&to=2020-01-07")]
    [InlineData("?from=2020-01-02&to=2020-01-01")]
    [InlineData("?from=2020-01-01&to=2021-01-01")]
    public async Task Invalid_period_returns_400_with_the_public_code(string query)
    {
        await using var app = await StartAsync();
        var admin = await app.CreateUserAsync(
            $"stats-period-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync($"{ENDPOINT}{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Contains(
            "admin.statistics.period",
            document.RootElement.GetProperty("errorCodes")
                .EnumerateArray()
                .Select(code => code.GetString() ?? string.Empty));
    }

    [Fact(Timeout = 180_000)]
    public async Task Counts_follow_utc_day_boundaries_previous_period_and_other_tail()
    {
        await using var app = await StartAsync();
        var data = new TestData(app);

        var city = $"Statistics-{Guid.NewGuid():N}";
        var barber = await data.CreateBookableBarberAsync("Statistics Barber", city);
        var first = await app.CreateUserAsync(
            $"stats-first-{Guid.NewGuid():N}@barbersearch.test");
        var second = await app.CreateUserAsync(
            $"stats-second-{Guid.NewGuid():N}@barbersearch.test");
        var third = await app.CreateUserAsync(
            $"stats-third-{Guid.NewGuid():N}@barbersearch.test");

        // Прошлый период: возвращающийся клиент и граница 23:59:59 последнего дня.
        await data.CreateBookingAsync(
            barber,
            third.Id,
            At(2019, 12, 28, 10, 0),
            BookingStatus.Completed,
            price: 500m);
        await data.CreateBookingAsync(
            barber,
            third.Id,
            At(2019, 12, 31, 23, 59, 59),
            BookingStatus.Completed,
            price: 999m);

        // Завершённый визит до периода: первый клиент становится вернувшимся.
        await data.CreateBookingAsync(
            barber,
            first.Id,
            At(2019, 12, 1, 10, 0),
            BookingStatus.Completed,
            price: 50m);

        // Первая секунда периода и последняя — обе входят.
        var firstVisit = await data.CreateBookingAsync(
            barber,
            first.Id,
            At(2020, 1, 1, 0, 0),
            BookingStatus.Completed,
            price: 100m);
        var lastVisit = await data.CreateBookingAsync(
            barber,
            second.Id,
            At(2020, 1, 7, 23, 59, 59),
            BookingStatus.Completed,
            price: 200m);
        await data.CreateBookingAsync(
            barber,
            second.Id,
            At(2020, 1, 6, 12, 0),
            BookingStatus.Completed,
            price: 300m);

        // День до периода и день после — не входят ни в текущий, ни в прошлый.
        await data.CreateBookingAsync(
            barber,
            third.Id,
            At(2019, 12, 24, 12, 0),
            BookingStatus.Completed,
            price: 999m);
        await data.CreateBookingAsync(
            barber,
            third.Id,
            At(2020, 1, 8, 0, 0),
            BookingStatus.Completed,
            price: 999m);

        await data.CreateBookingAsync(
            barber,
            first.Id,
            At(2020, 1, 3, 11, 0),
            BookingStatus.Cancelled);
        await data.CreateBookingAsync(
            barber,
            second.Id,
            At(2020, 1, 4, 11, 0),
            BookingStatus.NoShow);
        await data.CreateBookingAsync(
            barber,
            third.Id,
            At(2020, 1, 6, 15, 0),
            BookingStatus.Confirmed);

        // Зависшая бронь: активная, но старт давно в прошлом.
        await data.CreateBookingAsync(
            barber,
            first.Id,
            At(2020, 6, 1, 10, 0),
            BookingStatus.Pending);

        // Гостевая запись, созданная в периоде.
        var guest = await data.CreateGuestBookingAsync(
            barber,
            At(2020, 1, 5, 12, 0),
            BookingStatus.Cancelled);

        // «Создано записей» считается по created_at: переносим две брони и гостя.
        await data.SetBookingCreatedAtAsync(firstVisit.Id, At(2020, 1, 3, 8, 0));
        await data.SetBookingCreatedAtAsync(lastVisit.Id, At(2020, 1, 4, 8, 0));
        await data.SetBookingCreatedAtAsync(guest.Id, At(2020, 1, 5, 8, 0));

        var reviewId = await data.CreateReviewForBookingAsync(
            firstVisit.Id,
            first.Id,
            rating: 5,
            published: true);
        await data.SetReviewCreatedAtAsync(reviewId, At(2020, 1, 2, 9, 0));

        var admin = await app.CreateUserAsync(
            $"stats-numbers-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var comparedResponse = await app.Client.GetAsync(
            $"{ENDPOINT}?from=2020-01-01&to=2020-01-07&compare=true");
        Assert.Equal(HttpStatusCode.OK, comparedResponse.StatusCode);
        var statistics = await ReadAsync(comparedResponse);

        // Обзор: три завершённых визита, 600 ₽, средний чек 200.
        Assert.Equal(3, statistics.Overview.CompletedVisits.Value);
        Assert.Equal(2, statistics.Overview.CompletedVisits.Previous);
        Assert.Equal(600m, statistics.Overview.Turnover.Value);
        Assert.Equal(1499m, statistics.Overview.Turnover.Previous);
        Assert.Equal("RUB", statistics.Overview.Turnover.Currency);
        Assert.Equal(200m, statistics.Overview.AverageCheck.Value);
        Assert.Equal(749.5m, statistics.Overview.AverageCheck.Previous);
        Assert.Equal(0, statistics.Overview.NewAccounts.Value);
        Assert.Equal(0.25m, statistics.Overview.NoShowRate.Value);
        Assert.Equal(1, statistics.Overview.NoShowRate.Count);
        Assert.Equal(4, statistics.Overview.NoShowRate.Sample);
        Assert.Equal(2, statistics.Overview.StuckBookings.Value);

        Assert.Equal(
            new AdminStatisticsPeriodRangeResponse(
                new DateOnly(2019, 12, 25),
                new DateOnly(2019, 12, 31)),
            statistics.Period.Previous);

        // Статусы: зависшая бронь — вне периода, поэтому pending = 0; всё в периоде
        // уже началось, закрыто 3 + 2 + 1.
        Assert.Equal(0, statistics.Statuses.Pending);
        Assert.Equal(1, statistics.Statuses.Confirmed);
        Assert.Equal(3, statistics.Statuses.Completed);
        Assert.Equal(2, statistics.Statuses.Cancelled);
        Assert.Equal(1, statistics.Statuses.NoShow);
        Assert.Equal(7, statistics.Statuses.Due);
        Assert.Equal(6, statistics.Statuses.Closed);

        // Клиенты: два уникальных, один вернувшийся, один первый визит.
        Assert.Equal(2, statistics.Clients.WithVisit);
        Assert.Equal(1, statistics.Clients.Returning);
        Assert.Equal(1, statistics.Clients.FirstVisit);
        Assert.Equal(1, statistics.Clients.GuestBookings);
        Assert.Equal(0.3333m, statistics.Clients.GuestShare);
        Assert.Equal(0.3333m, statistics.Clients.ReviewRate);

        // Предложение: единственный профиль активен и работал.
        Assert.Equal(1, statistics.Supply.ActiveBarbers);
        Assert.Equal(1, statistics.Supply.WorkingBarbers);
        Assert.Equal(0, statistics.Supply.IdleBarbers);
        Assert.Equal(0, statistics.Supply.NewProfiles);
        var kind = Assert.Single(statistics.Supply.SalonsByKind);
        Assert.Equal("Salon", kind.Kind);
        Assert.Equal(1, kind.Listed);
        Assert.Equal(0, kind.Unlisted);

        // Топы: минимум три визита, хвост — остаток периода.
        var barberRow = Assert.Single(statistics.Top.Barbers.Rows);
        Assert.Equal(barber.Profile.Id, barberRow.BarberProfileId);
        Assert.Equal("Statistics Barber", barberRow.DisplayName);
        Assert.Equal(3, barberRow.Visits);
        Assert.Equal(600m, barberRow.Turnover);
        Assert.Equal(200m, barberRow.AverageCheck);
        Assert.Equal(1, barberRow.NoShow);
        Assert.Equal(0, statistics.Top.Barbers.Other.Visits);
        Assert.Equal(0m, statistics.Top.Barbers.Other.Turnover);

        var serviceRow = Assert.Single(statistics.Top.Services.Rows);
        Assert.Equal(barber.HairService.Id, serviceRow.HairServiceId);
        Assert.Equal(3, serviceRow.Visits);
        Assert.Equal(600m, serviceRow.Turnover);

        var cityRow = Assert.Single(statistics.Top.Cities.Rows);
        Assert.Equal(city, cityRow.Name);
        Assert.Equal(3, cityRow.Visits);
        Assert.Equal(600m, cityRow.Turnover);
        Assert.Equal(1, cityRow.ActiveBarbers);

        Assert.Equal(3, statistics.Top.Barbers.Rows.Sum(row => row.Visits)
            + statistics.Top.Barbers.Other.Visits);

        // Качество: один опубликованный отзыв периода.
        Assert.Equal(5m, statistics.Quality.Rating);
        Assert.Equal(1, statistics.Quality.Reviews);
        Assert.Equal(ExpectedDistribution, statistics.Quality.Distribution);
        Assert.Equal(1m, statistics.Quality.PublishedShare);
        Assert.Equal(0, statistics.Quality.Requests.Submitted);
        Assert.Equal(0, statistics.Quality.Salons.Submitted);

        // Плотный ряд: все семь дней, созданные — только перенесённые в период.
        Assert.Equal("day", statistics.Series.Granularity);
        Assert.Equal(7, statistics.Series.Points.Count);
        Assert.Equal(new DateOnly(2020, 1, 1), statistics.Series.Points[0].Date);
        Assert.Equal(1, statistics.Series.Points[0].Completed);
        Assert.Equal(1, statistics.Series.Points[2].Created);
        Assert.Equal(1, statistics.Series.Points[3].Created);
        Assert.Equal(1, statistics.Series.Points[4].Created);
        Assert.Equal(1, statistics.Series.Points[5].Completed);
        Assert.Equal(
            3,
            statistics.Series.Points.Sum(point => point.Created));

        // PII в теле: адресов клиентов нет.
        var rawBody = await comparedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(first.Email, rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(second.Email, rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(third.Email, rawBody, StringComparison.OrdinalIgnoreCase);

        // Без compare предыдущий период не отдаётся.
        using var plainResponse = await app.Client.GetAsync(
            $"{ENDPOINT}?from=2020-01-01&to=2020-01-07");
        var plain = await ReadAsync(plainResponse);

        Assert.Null(plain.Period.Previous);
        Assert.Null(plain.Overview.CompletedVisits.Previous);
        Assert.Null(plain.Overview.Turnover.Previous);
        Assert.Null(plain.Overview.NoShowRate.Previous);
    }

    [Fact(Timeout = 120_000)]
    public async Task Empty_period_returns_honest_zeros_and_nulls()
    {
        await using var app = await StartAsync();
        var admin = await app.CreateUserAsync(
            $"stats-empty-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(
            $"{ENDPOINT}?from=1990-01-01&to=1990-01-02");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statistics = await ReadAsync(response);

        Assert.Equal(0, statistics.Overview.CompletedVisits.Value);
        Assert.Equal(0m, statistics.Overview.Turnover.Value);
        Assert.Null(statistics.Overview.AverageCheck.Value);
        Assert.Null(statistics.Overview.NoShowRate.Value);
        Assert.Equal(0, statistics.Overview.NoShowRate.Sample);
        Assert.Null(statistics.Clients.GuestShare);
        Assert.Null(statistics.Clients.ReviewRate);
        Assert.Null(statistics.Quality.Rating);
        Assert.Null(statistics.Quality.PublishedShare);
        Assert.Empty(statistics.Top.Barbers.Rows);
        Assert.Empty(statistics.Top.Services.Rows);
        Assert.Empty(statistics.Top.Cities.Rows);
        Assert.Equal(2, statistics.Series.Points.Count);
        Assert.All(statistics.Series.Points, point => Assert.Equal(0, point.Created));
    }

    private static DateTimeOffset At(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second = 0)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private async Task<TestApp> StartAsync()
    {
        var time = new MutableTimeProvider();
        time.SetUtcNow(Now);

        return await TestApp.StartAsync(_database, timeProvider: time);
    }

    private static async Task<AdminStatisticsResponse> ReadAsync(
        HttpResponseMessage response)
    {
        var statistics = JsonSerializer.Deserialize<AdminStatisticsResponse>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.NotNull(statistics);

        return statistics;
    }
}
