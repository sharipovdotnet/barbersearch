using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
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
/// Сидирование через реальный <see cref="ApplicationDbContext"/>: доменные фабрики, никаких
/// SQL-вставок «в обход» модели.
/// </summary>
/// <remarks>
/// Рейтинг — исключение: его меняет только публикация отзыва через Application-обработчик,
/// а сценарию поиска нужен управляемый набор значений. Точечный
/// <c>UPDATE barber_profiles SET rating = …</c> допустим и не обходит проверки схемы
/// (rating numeric(3,2), ограничение диапазона остаются в силе).
/// </remarks>
internal sealed class TestData
{
    private readonly TestApp _app;

    public TestData(TestApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _app = app;
    }

    /// <summary>
    /// Создаёт пользователя-барбера и его профиль (активный, подтверждённый).
    /// </summary>
    public async Task<BarberProfile> CreateBarberProfileAsync(
        string displayName,
        string? email = null)
    {
        var user = await _app.CreateUserAsync(
            email ?? $"barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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

        Require(profile.Verify());

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return profile;
    }

    /// <summary>
    /// Создаёт барбера, салон и услугу — всё, что нужно для <c>POST api/bookings</c>.
    /// </summary>
    public async Task<SeededBarber> CreateBookableBarberAsync(
        string displayName,
        string city = "Berlin",
        string? email = null)
    {
        var user = await _app.CreateUserAsync(
            email ?? $"barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var salon = Require(Salon.Create(
            name: $"Salon {displayName}",
            address: "Teststrasse 1",
            city: city,
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
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null));

        Require(profile.Verify());
        Require(profile.AddSalon(salon.Id, isPrimary: true));
        Require(profile.AddService(hairService.Id, price: 45.00m, durationMinutes: 30));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededBarber(
            user,
            profile,
            salon,
            hairService,
            profile.Services.Single());
    }

    /// <summary>
    /// Создаёт барбера с двумя салонами: первый — основной. Нужно для проверки
    /// <c>SetPrimarySalon</c> и отложенного exclusion-ограничения (R-06).
    /// </summary>
    public async Task<SeededBarberWithSalons> CreateBarberWithSalonsAsync(
        string displayName,
        string email,
        string city = "Berlin")
    {
        var user = await _app.CreateUserAsync(email, UserRole.Barber);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var primary = Require(Salon.Create(
            name: $"{displayName} Primary",
            address: "Teststrasse 1",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5200,
            longitude: 13.4050));

        var secondary = Require(Salon.Create(
            name: $"{displayName} Secondary",
            address: "Teststrasse 2",
            city: city,
            country: "Germany",
            timeZoneId: "Europe/Berlin",
            latitude: 52.5300,
            longitude: 13.4150));

        db.Salons.AddRange(primary, secondary);
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

        Require(profile.Verify());
        Require(profile.AddSalon(primary.Id, isPrimary: true));
        Require(profile.AddSalon(secondary.Id));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededBarberWithSalons(user, profile, primary, secondary);
    }

    /// <summary>
    /// Проставляет сохранённый рейтинг и число отзывов — так, как это сделал бы обработчик
    /// публикации отзыва.
    /// </summary>
    public async Task SetRatingAsync(Guid barberProfileId, decimal rating, int reviewsCount = 1)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE barber_profiles
            SET rating = {rating}, reviews_count = {reviewsCount}
            WHERE id = {barberProfileId}
            """);

        Assert.Equal(1, updated);
    }

    /// <summary>
    /// Читает связи «барбер — салон» без кэша контекста: используется после HTTP-запроса.
    /// </summary>
    public async Task<IReadOnlyList<BarberSalon>> GetSalonLinksAsync(Guid barberProfileId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BarberSalons
            .AsNoTracking()
            .Where(link => link.BarberProfileId == barberProfileId)
            .OrderBy(link => link.Id)
            .ToListAsync();
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
