using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Расписание барбера на реальной PostgreSQL: чтение своего профиля (в том числе
/// неподтверждённого), замена недели, исключение на дату и его влияние на слоты.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberScheduleTests
{
    private readonly PostgresFixture _database;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public BarberScheduleTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Owner_reads_and_replaces_the_week_even_before_verification()
    {
        await using var app = await TestApp.StartAsync(_database);
        var barber = await SeedUnverifiedBarberAsync(app, "Unverified Schedule Barber");

        await app.AuthenticateAsync(barber.User.Email);

        using var before = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var initial = await before.Content.ReadFromJsonAsync<BarberScheduleResponse>(JsonOptions);
        Assert.NotNull(initial);
        var initialSalon = Assert.Single(initial.Salons);
        Assert.Equal(7, initialSalon.Weekly.Count);
        Assert.All(initialSalon.Weekly, day => Assert.True(day.IsDayOff));

        // DateOnly query-параметры окна разбираются MVC без строкового парсинга.
        using var window = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule?from=2026-11-01&to=2026-11-30");
        Assert.Equal(HttpStatusCode.OK, window.StatusCode);

        using var invertedWindow = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule?from=2026-11-30&to=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, invertedWindow.StatusCode);

        var days = Week(
            new TimeOnly(10, 0),
            new TimeOnly(20, 0),
            dayOff: DayOfWeek.Sunday);

        using var put = await app.Client.PutAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/working-schedules",
            new ReplaceWorkingSchedulesRequest(days),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        using var after = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var schedule = await after.Content.ReadFromJsonAsync<BarberScheduleResponse>(JsonOptions);
        Assert.NotNull(schedule);
        var salon = Assert.Single(schedule.Salons);
        var monday = Assert.Single(salon.Weekly, day => day.DayOfWeek == DayOfWeek.Monday);
        Assert.False(monday.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(20, 0), monday.EndTime);

        var sunday = Assert.Single(salon.Weekly, day => day.DayOfWeek == DayOfWeek.Sunday);
        Assert.True(sunday.IsDayOff);

        // Публичная карточка по-прежнему скрывает неподтверждённый профиль.
        using var publicCard = await app.Client.GetAsync($"api/barbers/{barber.Profile.Id}");
        Assert.Equal(HttpStatusCode.NotFound, publicCard.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Date_exception_overrides_the_week_and_delete_restores_it()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Override Schedule Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(14);
        var weeklyDay = date.DayOfWeek;

        var days = Week(
            new TimeOnly(10, 0),
            new TimeOnly(19, 0),
            dayOff: weeklyDay == DayOfWeek.Monday
                ? DayOfWeek.Tuesday
                : DayOfWeek.Monday);

        using var putWeek = await app.Client.PutAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/working-schedules",
            new ReplaceWorkingSchedulesRequest(days),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, putWeek.StatusCode);

        using var dayOff = await app.Client.PutAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/schedule-exceptions/{date:yyyy-MM-dd}",
            new SetScheduleExceptionRequest(
                IsDayOff: true,
                TimeOnly.MinValue,
                TimeOnly.MinValue,
                "Выходной"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, dayOff.StatusCode);

        var dayOffSlots = await GetSlotsAsync(app, barber.Profile.Id, barber.Salon.Id, date);
        Assert.False(dayOffSlots.IsWorkingDay);
        Assert.Empty(dayOffSlots.Slots);

        using var customHours = await app.Client.PutAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/schedule-exceptions/{date:yyyy-MM-dd}",
            new SetScheduleExceptionRequest(
                IsDayOff: false,
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "Короткий день"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, customHours.StatusCode);

        var customSlots = await GetSlotsAsync(app, barber.Profile.Id, barber.Salon.Id, date);
        Assert.True(customSlots.IsWorkingDay);
        Assert.Equal(4, customSlots.Slots.Count);

        using var delete = await app.Client.DeleteAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/schedule-exceptions/{date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var restoredSlots = await GetSlotsAsync(app, barber.Profile.Id, barber.Salon.Id, date);
        Assert.True(restoredSlots.IsWorkingDay);
        Assert.Equal(18, restoredSlots.Slots.Count);
    }

    [Fact(Timeout = 180_000)]
    public async Task Schedule_is_closed_for_anonymous_and_other_users()
    {
        await using var app = await TestApp.StartAsync(_database);
        var barber = await SeedUnverifiedBarberAsync(app, "Closed Schedule Barber");

        using var anonymous = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customer = await app.CreateUserAsync(
            $"schedule-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var forbidden = await app.Client.GetAsync(
            $"api/barber-profiles/{barber.Profile.Id}/schedule");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var days = Week(
            new TimeOnly(10, 0),
            new TimeOnly(20, 0),
            dayOff: DayOfWeek.Sunday);

        using var forbiddenWrite = await app.Client.PutAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/working-schedules",
            new ReplaceWorkingSchedulesRequest(days),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
    }

    private static async Task<BarberSlotsResponse> GetSlotsAsync(
        TestApp app,
        Guid barberProfileId,
        Guid salonId,
        DateOnly date)
    {
        using var response = await app.Client.GetAsync(
            $"api/barbers/{barberProfileId}/slots?date={date:yyyy-MM-dd}&salonId={salonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberSlotsResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static IReadOnlyList<ReplaceWorkingScheduleDayRequest> Week(
        TimeOnly start,
        TimeOnly end,
        DayOfWeek dayOff)
        =>
        [
            Day(DayOfWeek.Monday, start, end, dayOff),
            Day(DayOfWeek.Tuesday, start, end, dayOff),
            Day(DayOfWeek.Wednesday, start, end, dayOff),
            Day(DayOfWeek.Thursday, start, end, dayOff),
            Day(DayOfWeek.Friday, start, end, dayOff),
            Day(DayOfWeek.Saturday, start, end, dayOff),
            Day(DayOfWeek.Sunday, start, end, dayOff)
        ];

    private static ReplaceWorkingScheduleDayRequest Day(
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        DayOfWeek dayOff)
        => day == dayOff
            ? new ReplaceWorkingScheduleDayRequest(day, true, TimeOnly.MinValue, TimeOnly.MinValue)
            : new ReplaceWorkingScheduleDayRequest(day, false, start, end);

    private static async Task<SeededScheduleBarber> SeedUnverifiedBarberAsync(
        TestApp app,
        string displayName)
    {
        var user = await app.CreateUserAsync(
            $"unverified-schedule-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var salon = Require(Salon.Create(
            name: $"Salon {displayName}",
            address: "Teststrasse 1",
            city: "Berlin",
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5200,
            longitude: 13.4050));

        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        var profile = Require(BarberProfile.Create(
            userId: user.Id,
            displayName: displayName,
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.AddSalon(salon.Id, isPrimary: true));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededScheduleBarber(user, profile, salon);
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }

    private sealed record SeededScheduleBarber(User User, BarberProfile Profile, Salon Salon);
}
