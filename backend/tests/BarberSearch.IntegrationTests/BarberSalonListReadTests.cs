using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// <c>GET api/barber-profiles/{id}/salons</c>: владелец и администратор читают привязки,
/// чужие и аноним — нет; порядок — основной салон первым.
/// </summary>
/// <remarks>
/// Список не фильтруется по активности салона и по подтверждению профиля: «Мои салоны» —
/// рабочий экран барбера, а не витрина. <c>403</c> проверяется отдельным аккаунтом барбера:
/// роли <c>Barber</c> недостаточно, нужно владение профилем.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BarberSalonListReadTests
{
    private readonly PostgresFixture _database;

    private static readonly string[] SUMMARY_FIELDS =
    [
        "salonId",
        "name",
        "city",
        "address",
        "timeZoneId",
        "isPrimary",
        "country",
        "postalCode",
        "latitude",
        "longitude",
        "phoneNumber",
        "websiteUrl",
        "ownerUserId",
        "moderationStatus",
        "rejectionReason"
    ];

    private static readonly string[] OTHER_SALON_NAMES =
        ["Aaa Salon", "Hidden Salon", "Salon List Barber Secondary"];

    public BarberSalonListReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Owner_sees_every_linked_salon_with_the_primary_first()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var email = $"salon-list-owner-{Guid.NewGuid():N}@barbersearch.test";
        var seeded = await data.CreateBarberWithSalonsAsync("Salon List Barber", email);

        var alphabeticallyFirst = await data.CreateSalonAsync(
            "Aaa Salon",
            city: "Berlin",
            address: "A Street 1");
        var inactive = await data.CreateSalonAsync(
            "Hidden Salon",
            city: "Berlin",
            address: "B Street 2",
            isActive: false);

        await data.AttachSalonAsync(seeded.Profile.Id, alphabeticallyFirst.Id);
        await data.AttachSalonAsync(seeded.Profile.Id, inactive.Id);

        // Неподтверждённый профиль тоже видит свои салоны: их выбирают до верификации.
        await data.SetBarberProfileStateAsync(seeded.Profile.Id, isVerified: false, isActive: true);

        await app.AuthenticateAsync(email);

        using var response = await app.Client.GetAsync(
            $"api/barber-profiles/{seeded.Profile.Id}/salons");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.EnumerateArray().ToList();

        // Основной — первым; остальные — по названию. Выключенный салон не прячется.
        Assert.Equal(4, rows.Count);
        Assert.Equal(seeded.Primary.Id, rows[0].GetProperty("salonId").GetGuid());
        Assert.True(rows[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal(
            OTHER_SALON_NAMES,
            rows.Skip(1).Select(row => row.GetProperty("name").GetString()));

        var primary = rows[0];
        Assert.Equal(seeded.Primary.Name, primary.GetProperty("name").GetString());
        Assert.Equal(seeded.Primary.City, primary.GetProperty("city").GetString());
        Assert.Equal(seeded.Primary.Address, primary.GetProperty("address").GetString());
        Assert.Equal(seeded.Primary.TimeZoneId, primary.GetProperty("timeZoneId").GetString());
        Assert.Equal(
            SUMMARY_FIELDS,
            primary.EnumerateObject().Select(property => property.Name));
    }

    [Fact(Timeout = 180_000)]
    public async Task List_is_closed_for_anonymous_other_barbers_and_missing_profiles()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var seeded = await data.CreateBarberWithSalonsAsync(
            "Closed Salon List Barber",
            $"salon-list-closed-{Guid.NewGuid():N}@barbersearch.test");

        using (var anonymous = await app.Client.GetAsync(
                   $"api/barber-profiles/{seeded.Profile.Id}/salons"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // Другой барбер: роль подходит, владение — нет.
        var otherEmail = $"salon-list-other-{Guid.NewGuid():N}@barbersearch.test";
        await data.CreateBarberProfileAsync("Other Salon List Barber", otherEmail);
        await app.AuthenticateAsync(otherEmail);
        using (var forbidden = await app.Client.GetAsync(
                   $"api/barber-profiles/{seeded.Profile.Id}/salons"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // Администратор читает чужой профиль.
        var admin = await app.CreateUserAsync(
            $"salon-list-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using (var asAdmin = await app.Client.GetAsync(
                   $"api/barber-profiles/{seeded.Profile.Id}/salons"))
        {
            Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);

            using var adminDocument = JsonDocument.Parse(
                await asAdmin.Content.ReadAsStringAsync());
            Assert.Equal(2, adminDocument.RootElement.GetArrayLength());
        }

        using (var missing = await app.Client.GetAsync(
                   $"api/barber-profiles/{Guid.NewGuid()}/salons"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }
}
