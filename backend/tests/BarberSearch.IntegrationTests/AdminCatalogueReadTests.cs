using System.Net;
using System.Text.Json;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админское чтение каталога на реальной PostgreSQL: полный справочник (включая выключенные
/// строки), фильтр статуса, поиск с экранированием шаблона, <c>usageCount</c> и закрытость
/// от не-администраторов.
/// </summary>
/// <remarks>
/// Справочники маленькие, поэтому ответ — голый JSON-массив без пагинации: порядок строк
/// входит в контракт (услуги — категория, имя; специализации — имя; салоны — город, имя),
/// и тесты сравнивают последовательность <c>id</c>, а не только код ответа.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminCatalogueReadTests
{
    private const string HAIR_SERVICES = "api/admin/catalogue/hair-services";
    private const string SPECIALIZATIONS = "api/admin/catalogue/specializations";
    private const string SALONS = "api/admin/catalogue/salons";

    private const string STATUS_ERROR = "admin.catalogue.status";
    private const string SEARCH_ERROR = "admin.catalogue.search";

    private static readonly string[] READ_URLS = [HAIR_SERVICES, SPECIALIZATIONS, SALONS];

    private readonly PostgresFixture _database;

    public AdminCatalogueReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Anonymous_gets_401_and_non_admin_gets_403_on_every_catalogue_read()
    {
        await using var app = await TestApp.StartAsync(_database);

        foreach (var url in READ_URLS)
        {
            using var anonymous = await app.Client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var customer = await app.CreateUserAsync(
            $"catalogue-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        foreach (var url in READ_URLS)
        {
            using var forbidden = await app.Client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var barber = await app.CreateUserAsync(
            $"catalogue-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        foreach (var url in READ_URLS)
        {
            using var forbidden = await app.Client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var admin = await app.CreateUserAsync(
            $"catalogue-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        foreach (var url in READ_URLS)
        {
            using var allowed = await app.Client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task All_status_includes_hidden_rows_in_every_dictionary()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var activeService = await data.CreateHairServiceAsync("Visible Service");
        var hiddenService = await data.CreateHairServiceAsync("Hidden Service", isActive: false);
        var activeSpecialization = await data.CreateSpecializationAsync("Visible Specialization");
        var hiddenSpecialization = await data.CreateSpecializationAsync(
            "Hidden Specialization",
            isActive: false);
        var activeSalon = await data.CreateSalonAsync("Visible Salon");
        var hiddenSalon = await data.CreateSalonAsync("Hidden Salon", isActive: false);

        await SignInAsAdminAsync(app);

        // status не передан: по умолчанию all, иначе выключенную строку не найти для возврата.
        var services = await ReadRowsAsync(app.Client, HAIR_SERVICES);
        Assert.Contains(activeService.Id, Ids(services));
        Assert.Contains(hiddenService.Id, Ids(services));
        Assert.False(Row(services, hiddenService.Id).GetProperty("isActive").GetBoolean());

        var specializations = await ReadRowsAsync(app.Client, SPECIALIZATIONS);
        Assert.Contains(activeSpecialization.Id, Ids(specializations));
        Assert.Contains(hiddenSpecialization.Id, Ids(specializations));
        Assert.False(
            Row(specializations, hiddenSpecialization.Id).GetProperty("isActive").GetBoolean());

        var salons = await ReadRowsAsync(app.Client, SALONS);
        Assert.Contains(activeSalon.Id, Ids(salons));
        Assert.Contains(hiddenSalon.Id, Ids(salons));
        Assert.False(Row(salons, hiddenSalon.Id).GetProperty("isActive").GetBoolean());
    }

    [Fact(Timeout = 120_000)]
    public async Task Status_filter_splits_active_and_hidden()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var activeService = await data.CreateHairServiceAsync("Active Service");
        var hiddenService = await data.CreateHairServiceAsync("Hidden Service", isActive: false);
        var activeSpecialization = await data.CreateSpecializationAsync("Active Specialization");
        var hiddenSpecialization = await data.CreateSpecializationAsync(
            "Hidden Specialization",
            isActive: false);
        var activeSalon = await data.CreateSalonAsync("Active Salon");
        var hiddenSalon = await data.CreateSalonAsync("Hidden Salon", isActive: false);

        await SignInAsAdminAsync(app);

        Assert.Equal(
            [activeService.Id],
            Ids(await ReadRowsAsync(app.Client, $"{HAIR_SERVICES}?status=active")));
        Assert.Equal(
            [hiddenService.Id],
            Ids(await ReadRowsAsync(app.Client, $"{HAIR_SERVICES}?status=hidden")));
        Assert.Equal(
            new[] { activeService.Id, hiddenService.Id }.Order(),
            Ids(await ReadRowsAsync(app.Client, $"{HAIR_SERVICES}?status=all")).Order());

        Assert.Equal(
            [activeSpecialization.Id],
            Ids(await ReadRowsAsync(app.Client, $"{SPECIALIZATIONS}?status=active")));
        Assert.Equal(
            [hiddenSpecialization.Id],
            Ids(await ReadRowsAsync(app.Client, $"{SPECIALIZATIONS}?status=hidden")));

        Assert.Equal(
            [activeSalon.Id],
            Ids(await ReadRowsAsync(app.Client, $"{SALONS}?status=active")));
        Assert.Equal(
            [hiddenSalon.Id],
            Ids(await ReadRowsAsync(app.Client, $"{SALONS}?status=hidden")));
    }

    [Fact(Timeout = 180_000)]
    public async Task Usage_counts_only_active_verified_profiles()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var service = await data.CreateHairServiceAsync("Shared Service");
        var specialization = await data.CreateSpecializationAsync("Shared Specialization");
        var salon = await data.CreateSalonAsync("Shared Salon");

        // Три барбера со связями: считается только активный и подтверждённый.
        var counted = await data.CreateBarberProfileAsync("Counted Barber");
        var unverified = await data.CreateBarberProfileAsync("Unverified Barber");
        var inactive = await data.CreateBarberProfileAsync("Inactive Barber");

        foreach (var profile in new[] { counted, unverified, inactive })
        {
            await data.AttachHairServiceAsync(profile.Id, service.Id);
            await data.AddSpecializationAsync(profile.Id, specialization.Id);
            await data.AttachSalonAsync(profile.Id, salon.Id);
        }

        await data.SetBarberProfileStateAsync(unverified.Id, isVerified: false, isActive: true);
        await data.SetBarberProfileStateAsync(inactive.Id, isVerified: true, isActive: false);

        await SignInAsAdminAsync(app);

        Assert.Equal(
            1,
            UsageCount(await ReadRowsAsync(app.Client, HAIR_SERVICES), service.Id));
        Assert.Equal(
            1,
            UsageCount(await ReadRowsAsync(app.Client, SPECIALIZATIONS), specialization.Id));
        Assert.Equal(1, UsageCount(await ReadRowsAsync(app.Client, SALONS), salon.Id));

        // Выключенная связь «барбер — услуга» тоже не считается: у специальности и салона
        // такой связи нет, поэтому их счётчики не меняются.
        await data.DeactivateServiceLinkAsync(counted.Id, service.Id);

        Assert.Equal(
            0,
            UsageCount(await ReadRowsAsync(app.Client, HAIR_SERVICES), service.Id));
        Assert.Equal(
            1,
            UsageCount(await ReadRowsAsync(app.Client, SPECIALIZATIONS), specialization.Id));
        Assert.Equal(1, UsageCount(await ReadRowsAsync(app.Client, SALONS), salon.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task Search_is_case_insensitive_and_escapes_wildcards()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var haircut = await data.CreateHairServiceAsync("E2E Стрижка", ServiceCategory.Haircut);
        var percent = await data.CreateHairServiceAsync(
            "Скидка 100% на всё",
            ServiceCategory.Other);
        var underscore = await data.CreateHairServiceAsync(
            "Фейд_с_подчёркиванием",
            ServiceCategory.Beard);

        await SignInAsAdminAsync(app);

        // «стрижка» находит «E2E Стрижка»: регистр не важен.
        Assert.Equal(
            [haircut.Id],
            Ids(await ReadRowsAsync(
                app.Client,
                $"{HAIR_SERVICES}?search={Uri.EscapeDataString("стрижка")}")));

        // «%» — литерал, а не «найти всё»: шаблон находит только строку с символом процента.
        var percentRows = await ReadRowsAsync(
            app.Client,
            $"{HAIR_SERVICES}?search={Uri.EscapeDataString("%")}");
        Assert.Equal([percent.Id], Ids(percentRows));

        // «_» — тоже литерал: без экранирования он совпал бы с любым символом.
        Assert.Equal(
            [underscore.Id],
            Ids(await ReadRowsAsync(
                app.Client,
                $"{HAIR_SERVICES}?search={Uri.EscapeDataString("_")}")));

        // Поиск по точному проценту вместе с числом не превращается в «100 любых символов».
        Assert.Equal(
            [percent.Id],
            Ids(await ReadRowsAsync(
                app.Client,
                $"{HAIR_SERVICES}?search={Uri.EscapeDataString("100%")}")));
    }

    [Fact(Timeout = 120_000)]
    public async Task Salon_search_matches_city_as_well_as_name()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var moscow = await data.CreateSalonAsync("E2E Studio", city: "Москва");
        var kazan = await data.CreateSalonAsync("E2E Nord", city: "Казань");

        await SignInAsAdminAsync(app);

        Assert.Equal(
            [kazan.Id],
            Ids(await ReadRowsAsync(
                app.Client,
                $"{SALONS}?search={Uri.EscapeDataString("казань")}")));
        Assert.Equal(
            [moscow.Id],
            Ids(await ReadRowsAsync(
                app.Client,
                $"{SALONS}?search={Uri.EscapeDataString("москва")}")));
    }

    [Fact(Timeout = 120_000)]
    public async Task Lists_are_ordered_for_scanning()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Порядок вставки перемешан: проверяем именно сортировку ответа.
        var beardZeta = await data.CreateHairServiceAsync("Zeta Beard", ServiceCategory.Beard);
        var haircutAlpha = await data.CreateHairServiceAsync(
            "Alpha Haircut",
            ServiceCategory.Haircut);
        var beardAlpha = await data.CreateHairServiceAsync("Alpha Beard", ServiceCategory.Beard);

        var zetaSpecialization = await data.CreateSpecializationAsync("Zeta Specialization");
        var alphaSpecialization = await data.CreateSpecializationAsync("Alpha Specialization");

        var berlinZeta = await data.CreateSalonAsync("Zeta Salon", city: "Berlin");
        var amsterdamAlpha = await data.CreateSalonAsync("Alpha Salon", city: "Amsterdam");
        var berlinAlpha = await data.CreateSalonAsync("Alpha Salon", city: "Berlin");

        await SignInAsAdminAsync(app);

        Assert.Equal(
            [haircutAlpha.Id, beardAlpha.Id, beardZeta.Id],
            Ids(await ReadRowsAsync(app.Client, HAIR_SERVICES)));
        Assert.Equal(
            [alphaSpecialization.Id, zetaSpecialization.Id],
            Ids(await ReadRowsAsync(app.Client, SPECIALIZATIONS)));
        Assert.Equal(
            [amsterdamAlpha.Id, berlinAlpha.Id, berlinZeta.Id],
            Ids(await ReadRowsAsync(app.Client, SALONS)));
    }

    [Fact(Timeout = 120_000)]
    public async Task Salon_row_carries_every_edit_field()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var salon = await data.CreateSalonAsync(
            name: "Nord Studio",
            city: "Санкт-Петербург",
            address: "ул. Северная, 2",
            country: "Russia",
            timeZoneId: "Europe/Moscow",
            latitude: 59.9386,
            longitude: 30.3141,
            postalCode: "190000",
            phoneNumber: "+78120000000",
            websiteUrl: "https://nord.example/studio");

        await SignInAsAdminAsync(app);

        var row = Row(await ReadRowsAsync(app.Client, SALONS), salon.Id);

        Assert.Equal("Nord Studio", row.GetProperty("name").GetString());
        Assert.Equal("ул. Северная, 2", row.GetProperty("address").GetString());
        Assert.Equal("Санкт-Петербург", row.GetProperty("city").GetString());
        Assert.Equal("190000", row.GetProperty("postalCode").GetString());
        Assert.Equal("Russia", row.GetProperty("country").GetString());
        Assert.Equal("Europe/Moscow", row.GetProperty("timeZoneId").GetString());
        Assert.Equal(59.9386, row.GetProperty("latitude").GetDouble(), 4);
        Assert.Equal(30.3141, row.GetProperty("longitude").GetDouble(), 4);
        Assert.Equal("+78120000000", row.GetProperty("phoneNumber").GetString());
        Assert.Equal("https://nord.example/studio", row.GetProperty("websiteUrl").GetString());
        Assert.True(row.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, row.GetProperty("usageCount").GetInt32());
    }

    [Fact(Timeout = 180_000)]
    public async Task Unknown_status_is_rejected_with_400()
    {
        await using var app = await TestApp.StartAsync(_database);

        await SignInAsAdminAsync(app);

        foreach (var url in READ_URLS)
        {
            foreach (var status in new[] { "wrong", "up", "0" })
            {
                using var response = await app.Client.GetAsync($"{url}?status={status}");

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains(STATUS_ERROR, await response.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Search_longer_than_100_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);

        await SignInAsAdminAsync(app);

        var tooLong = new string('a', 101);
        var atLimit = new string('a', 100);

        foreach (var url in READ_URLS)
        {
            using var rejected = await app.Client.GetAsync(
                $"{url}?search={Uri.EscapeDataString(tooLong)}");

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains(SEARCH_ERROR, await rejected.Content.ReadAsStringAsync());

            using var accepted = await app.Client.GetAsync(
                $"{url}?search={Uri.EscapeDataString(atLimit)}");

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Hiding_a_row_keeps_the_junction_rows_and_the_admin_list()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Hidden Link Barber");

        await SignInAsAdminAsync(app);

        using var hidden = await app.Client.DeleteAsync(
            $"api/hair-services/{barber.HairService.Id}/activation");
        Assert.Equal(HttpStatusCode.NoContent, hidden.StatusCode);

        // У админа строка остаётся, просто выключенной: её нужно видеть, чтобы включить обратно.
        var adminRow = Row(
            await ReadRowsAsync(app.Client, HAIR_SERVICES),
            barber.HairService.Id);
        Assert.False(adminRow.GetProperty("isActive").GetBoolean());

        // Публичное чтение выключенную строку не отдаёт.
        var publicRows = await ReadRowsAsync(app.Client, "api/hair-services");
        Assert.DoesNotContain(barber.HairService.Id, Ids(publicRows));

        // Связи барбера не удалены: выключение — флаг, а не каскад.
        Assert.Equal(1, await data.CountHairServiceLinksAsync(barber.HairService.Id));
    }

    private static async Task SignInAsAdminAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"catalogue-read-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadRowsAsync(
        HttpClient client,
        string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToList();
    }

    private static Guid[] Ids(IReadOnlyList<JsonElement> rows)
        => rows.Select(row => row.GetProperty("id").GetGuid()).ToArray();

    private static JsonElement Row(IReadOnlyList<JsonElement> rows, Guid id)
        => Assert.Single(rows, row => row.GetProperty("id").GetGuid() == id);

    private static int UsageCount(IReadOnlyList<JsonElement> rows, Guid id)
        => Row(rows, id).GetProperty("usageCount").GetInt32();
}
