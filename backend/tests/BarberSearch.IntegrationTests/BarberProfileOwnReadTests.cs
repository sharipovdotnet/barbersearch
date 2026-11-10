using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
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
/// Собственная карточка барбера на реальной PostgreSQL: чтение до подтверждения, скрытые
/// услуги и права владельца/чужого/анонима.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberProfileOwnReadTests
{
    /// <summary>
    /// Сервер отдаёт перечисления строками, поэтому клиенту нужен конвертер.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberProfileOwnReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Owner_reads_own_card_before_verification_including_hidden_services()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seeded = await SeedUnverifiedBarberAsync(app, "Unverified Profile Barber");

        await app.AuthenticateAsync(seeded.User.Email);

        using var response = await app.Client.GetAsync(
            $"api/barber-profiles/{seeded.Profile.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);
        Assert.Equal(seeded.Profile.Id, card.Id);
        Assert.Equal(seeded.User.Id, card.UserId);
        Assert.Equal("Unverified Profile Barber", card.DisplayName);
        Assert.False(card.IsVerified);

        // Скрытая услуга обязана прийти в собственном чтении: иначе барбер не увидит,
        // что она у него есть, и не сможет вернуть её в прайс.
        var service = Assert.Single(card.Services);
        Assert.Equal(seeded.HairService.Id, service.HairServiceId);
        Assert.Equal(1500m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
        Assert.False(service.IsActive);

        // Публичная карточка тот же профиль по-прежнему скрывает.
        using var publicCard = await app.Client.GetAsync($"api/barbers/{seeded.Profile.Id}");
        Assert.Equal(HttpStatusCode.NotFound, publicCard.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Own_card_is_forbidden_for_another_barber()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seeded = await SeedUnverifiedBarberAsync(app, "Owned Profile Barber");
        var stranger = await app.CreateUserAsync(
            $"profile-stranger-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await app.AuthenticateAsync(stranger.Email);

        using var response = await app.Client.GetAsync(
            $"api/barber-profiles/{seeded.Profile.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Own_card_is_unauthorized_for_anonymous()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seeded = await SeedUnverifiedBarberAsync(app, "Anonymous Profile Barber");

        using var response = await app.Client.GetAsync(
            $"api/barber-profiles/{seeded.Profile.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// R-42 пересмотрен: у созданного профиля есть читаемый self-URI, поэтому
    /// <c>POST api/barber-profiles</c> отвечает <c>201</c> с заголовком <c>Location</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Created_profile_answers_201_with_a_readable_location()
    {
        await using var app = await TestApp.StartAsync(_database);
        var user = await app.CreateUserAsync(
            $"profile-create-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(user.Email);

        using var create = await app.Client.PostAsJsonAsync(
            "api/barber-profiles",
            new BarberProfileCreateRequest(
                DisplayName: "Created Profile Barber",
                YearsOfExperience: 5,
                Bio: "Bio",
                Languages: [Language.Ru],
                CertificatesAndTraining: [],
                Achievements: [],
                PaymentMethods: [PaymentMethod.Cash],
                CommunicationFormat: [],
                Goodies: [],
                LatePolicy: null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(JsonOptions);
        Assert.NotNull(created);

        // CreatedAtAction строит URI через LinkGenerator: в тестовом хосте он абсолютный,
        // в проде зависит от схемы/хоста запроса. Проверяем путь, а не форму записи.
        var location = create.Headers.Location;
        Assert.NotNull(location);
        var locationPath = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        Assert.Equal($"/api/barber-profiles/{created.Id}", locationPath);

        using var read = await app.Client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var card = await read.Content.ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
        Assert.NotNull(card);
        Assert.Equal(created.Id, card.Id);
        Assert.False(card.IsVerified);
    }

    private static async Task<SeededProfileBarber> SeedUnverifiedBarberAsync(
        TestApp app,
        string displayName)
    {
        var user = await app.CreateUserAsync(
            $"unverified-profile-{Guid.NewGuid():N}@barbersearch.test",
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
        Require(profile.AddService(hairService.Id, price: 1500m, durationMinutes: 45));
        Require(profile.DeactivateService(hairService.Id));

        db.BarberProfiles.Add(profile);
        await db.SaveChangesAsync();

        return new SeededProfileBarber(user, profile, salon, hairService);
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

    private sealed record SeededProfileBarber(
        User User,
        BarberProfile Profile,
        Salon Salon,
        HairService HairService);
}
