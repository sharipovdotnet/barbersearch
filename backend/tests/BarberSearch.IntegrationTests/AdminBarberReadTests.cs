using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.HairServices;
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
/// Админское чтение барберов на реальной PostgreSQL: очередь проверки, поиск по имени
/// и email, карточка с чек-листом готовности и закрытость от не-администраторов.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdminBarberReadTests
{
    private readonly PostgresFixture _database;

    /// <summary>
    /// API отдаёт перечисления строками (<c>Grade</c>, <c>UserRole</c>), а клиент
    /// <see cref="HttpClient"/> по умолчанию ждёт числа: без этого конвертера чтение
    /// <c>Grade</c> падает на первом же ответе.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminBarberReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_reads_pending_barber_and_readiness_checklist()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var pending = await SeedPendingBarberAsync(app, "Pending Barber");
        var verified = await data.CreateBookableBarberAsync("Verified Barber");
        var admin = await app.CreateUserAsync(
            $"admin-barbers-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        using var queue = await app.Client.GetAsync("api/admin/barbers?status=pending");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);

        var page = await queue.Content.ReadFromJsonAsync<PagedResponse<AdminBarberResponse>>(JsonOptions);
        Assert.NotNull(page);

        var item = Assert.Single(page.Items);
        Assert.Equal(pending.Profile.Id, item.Id);
        Assert.Equal("Pending Barber", item.DisplayName);
        Assert.Equal(pending.User.Email, item.Email);
        Assert.False(item.IsVerified);
        Assert.True(item.IsActive);
        Assert.Null(item.Rating);

        // Поиск идёт по имени профиля; подтверждённый барбер находится по нему же.
        using var search = await app.Client.GetAsync("api/admin/barbers?search=verified");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var searchPage = await search.Content.ReadFromJsonAsync<PagedResponse<AdminBarberResponse>>(JsonOptions);
        Assert.NotNull(searchPage);

        var found = Assert.Single(searchPage.Items);
        Assert.Equal(verified.Profile.Id, found.Id);
        Assert.True(found.IsVerified);

        using var detail = await app.Client.GetAsync($"api/admin/barbers/{pending.Profile.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<AdminBarberDetailResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(pending.Profile.Id, body.Id);
        Assert.Equal(pending.User.Email, body.Email);
        Assert.False(body.IsVerified);
        Assert.Equal("Проверяю себя на длинных фейдах.", body.Bio);
        Assert.Equal(8, body.YearsOfExperience);

        // Чек-лист: описание и опыт есть, email не подтверждён, по одному профилю на счётчик.
        Assert.True(body.Readiness.IsProfileFilled);
        Assert.False(body.Readiness.IsEmailVerified);
        Assert.Equal(1, body.Readiness.ServicesCount);
        Assert.Equal(1, body.Readiness.SalonsCount);
        Assert.Equal(2, body.Readiness.ScheduleDaysCount);
        Assert.Equal(1, body.Readiness.PhotosCount);
    }

    [Fact(Timeout = 120_000)]
    public async Task Admin_barbers_are_closed_for_anonymous_and_customers()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymous = await app.Client.GetAsync("api/admin/barbers");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customer = await app.CreateUserAsync(
            $"admin-barbers-forbidden-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var forbidden = await app.Client.GetAsync("api/admin/barbers");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<SeededBarber> SeedPendingBarberAsync(
        TestApp app,
        string displayName)
    {
        var user = await app.CreateUserAsync(
            $"pending-{Guid.NewGuid():N}@barbersearch.test",
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

        var hairService = Require(HairService.Create(
            name: $"Haircut {displayName}",
            category: ServiceCategory.Haircut,
            defaultDurationMinutes: 30));

        db.Salons.Add(salon);
        db.HairServices.Add(hairService);
        await db.SaveChangesAsync();

        var profile = Require(BarberProfile.Create(
            userId: user.Id,
            displayName: displayName,
            yearsOfExperience: 8,
            bio: "Проверяю себя на длинных фейдах.",
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.AddSalon(salon.Id, isPrimary: true));
        Require(profile.AddService(hairService.Id, price: 45.00m, durationMinutes: 30));
        Require(profile.AddPhoto("https://example.com/portfolio/fade.jpg"));

        var link = Require(profile.FindSalon(salon.Id));
        Require(link.AddWorkingSchedule(DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(19, 0)));
        Require(link.AddWorkingSchedule(DayOfWeek.Tuesday, new TimeOnly(10, 0), new TimeOnly(19, 0)));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededBarber(
            user,
            profile,
            salon,
            hairService,
            profile.Services.Single());
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
}
