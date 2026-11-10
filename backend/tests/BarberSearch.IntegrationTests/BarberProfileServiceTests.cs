using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Услуги в профиле барбера на реальной PostgreSQL: скрытие не рвёт историю записей,
/// реактивация той же услуги и правка цены/длительности отдельным <c>PUT</c>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberProfileServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberProfileServiceTests(PostgresFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// Скрытие услуги — деактивация: строка остаётся в БД, поэтому брони, сделанные на неё,
    /// не исчезают из списков барбера и админки, а публичная карточка услугу не показывает.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Hidden_service_keeps_the_booking_in_barber_and_admin_lists()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Hidden Service Barber");
        await LinkBarberProfileAsync(app, barber);

        await app.AuthenticateAsync(barber.User.Email);

        // Бронь создаётся до скрытия услуги: у сидового барбера записей нет.
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        using (var createBooking = await app.Client.PostAsJsonAsync(
                   "api/barber/bookings",
                   new CreateBarberBookingRequest(
                       CustomerId: null,
                       GuestFirstName: "Гость",
                       GuestLastName: null,
                       GuestPhone: "+70000000000",
                       BarberServiceId: barber.Service.Id,
                       SalonId: barber.Salon.Id,
                       StartAt: startAt,
                       EndAt: startAt.AddMinutes(30),
                       IdempotencyKey: $"hidden-service-{Guid.NewGuid():N}",
                       CustomerNote: null)))
        {
            Assert.Equal(HttpStatusCode.Created, createBooking.StatusCode);
        }

        using (var hide = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services/{barber.HairService.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, hide.StatusCode);
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.BarberServices
                .AsNoTracking()
                .SingleAsync(service => service.BarberProfileId == barber.Profile.Id
                                        && service.HairServiceId == barber.HairService.Id);

            Assert.False(row.IsActive);
            Assert.Equal(barber.Service.Id, row.Id);
        }

        // Публичная карточка режет скрытую услугу серверным фильтром LoadServicesAsync.
        using (var publicCard = await app.Client.GetAsync($"api/barbers/{barber.Profile.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, publicCard.StatusCode);

            var card = await publicCard.Content
                .ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
            Assert.NotNull(card);
            Assert.Empty(card.Services);
        }

        // Списки броней строятся внутренними join'ами bookings → barber_services → hair_services:
        // физическое удаление строки выкинуло бы бронь, деактивация — нет.
        using (var barberBookings = await app.Client.GetAsync("api/barber/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, barberBookings.StatusCode);

            var page = await barberBookings.Content
                .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
            Assert.NotNull(page);
            var item = Assert.Single(page.Items);
            Assert.Equal("Haircut Hidden Service Barber", item.Service.Name);
        }

        var admin = await app.CreateUserAsync(
            $"hidden-service-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var adminBookings = await app.Client.GetAsync("api/admin/bookings"))
        {
            Assert.Equal(HttpStatusCode.OK, adminBookings.StatusCode);

            var page = await adminBookings.Content
                .ReadFromJsonAsync<PagedResponse<AdminBookingResponse>>(JsonOptions);
            Assert.NotNull(page);
            var item = Assert.Single(page.Items);
            Assert.Equal("Haircut Hidden Service Barber", item.Service.Name);
        }
    }

    /// <summary>
    /// Возврат услуги в прайс — <c>POST</c> с той же услугой справочника: строка реактивируется,
    /// а цена и длительность перезаписываются. <c>PUT</c> правит их без смены видимости.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Reactivation_restores_the_row_and_update_changes_price_and_duration()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reactivated Service Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using (var hide = await app.Client.DeleteAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services/{barber.HairService.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, hide.StatusCode);
        }

        using (var reactivate = await app.Client.PostAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services",
                   new AddBarberProfileServiceRequest(
                       barber.HairService.Id,
                       Price: 2000m,
                       DurationMinutes: 60)))
        {
            Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        }

        await AssertServiceRowAsync(app, barber, price: 2000m, durationMinutes: 60, isActive: true);

        using (var update = await app.Client.PutAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services/{barber.HairService.Id}",
                   new UpdateBarberProfileServiceRequest(
                       Price: 2500m,
                       DurationMinutes: 75),
                   JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        await AssertServiceRowAsync(app, barber, price: 2500m, durationMinutes: 75, isActive: true);

        // Собственная карточка показывает новую цену и длительность.
        using (var ownCard = await app.Client.GetAsync(
                   $"api/barber-profiles/{barber.Profile.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, ownCard.StatusCode);

            var card = await ownCard.Content
                .ReadFromJsonAsync<BarberProfileResponse>(JsonOptions);
            Assert.NotNull(card);
            var service = Assert.Single(card.Services);
            Assert.Equal(2500m, service.Price);
            Assert.Equal(75, service.DurationMinutes);
        }

        using (var unknown = await app.Client.PutAsJsonAsync(
                   $"api/barber-profiles/{barber.Profile.Id}/services/{Guid.NewGuid()}",
                   new UpdateBarberProfileServiceRequest(
                       Price: 2500m,
                       DurationMinutes: 75),
                   JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Contains(
                "barberProfile.updateService.notFound",
                await unknown.Content.ReadAsStringAsync());
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Adding_an_already_active_service_returns_duplicate_conflict()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Duplicate Service Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using var duplicate = await app.Client.PostAsJsonAsync(
            $"api/barber-profiles/{barber.Profile.Id}/services",
            new AddBarberProfileServiceRequest(
                barber.HairService.Id,
                Price: 2000m,
                DurationMinutes: 60));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "barberProfile.addService.duplicate",
            await duplicate.Content.ReadAsStringAsync());
    }

    private static async Task AssertServiceRowAsync(
        TestApp app,
        SeededBarber barber,
        decimal price,
        int durationMinutes,
        bool isActive)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.BarberServices
            .AsNoTracking()
            .SingleAsync(service => service.BarberProfileId == barber.Profile.Id
                                    && service.HairServiceId == barber.HairService.Id);

        Assert.Equal(price, row.Price);
        Assert.Equal(durationMinutes, row.DurationMinutes);
        Assert.Equal(isActive, row.IsActive);
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
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
