using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.CreateHairService;
using BarberSearch.Contracts.HairServices.UpdateHairService;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Contracts.Specializations.CreateSpecialization;
using BarberSearch.Contracts.Specializations.RenameSpecialization;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Мутации каталога, на которые опирается админский экран: создание, правка, переименование
/// и правила домена (категория не меняется, имя специализации уникально, часовой пояс
/// разрешается), плюс границы прав.
/// </summary>
/// <remarks>
/// Эндпоинты существующие — этап не добавляет мутаций. Тесты фиксируют то, что экран
/// обещает пользователю: созданная строка сразу видна в админском списке, а ошибки приходят
/// публичными кодами, а не текстом.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminCatalogueWriteTests
{
    private const string HAIR_SERVICES = "api/admin/catalogue/hair-services";
    private const string SPECIALIZATIONS = "api/admin/catalogue/specializations";
    private const string SALONS = "api/admin/catalogue/salons";

    private readonly PostgresFixture _database;

    /// <summary>
    /// API читает перечисления строками и отдаёт их строками: без этого конвертера
    /// <c>category</c> ушёл бы числом, и model binding вернул бы <c>400</c> до обработчика.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminCatalogueWriteTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Create_appears_in_the_admin_list()
    {
        await using var app = await TestApp.StartAsync(_database);
        await SignInAsAdminAsync(app);

        var createdService = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/hair-services",
            new CreateHairServiceRequest("Catalogue Fade", ServiceCategory.Beard, 45, "Seeded"));
        var createdSpecialization = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/specializations",
            new CreateSpecializationRequest("Catalogue Skin Fade", "Seeded"));
        var createdSalon = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/salons",
            new CreateSalonRequest(
                "Catalogue Salon",
                "ул. Профсоюзная, 1",
                "Казань",
                "Russia",
                "Europe/Moscow",
                55.7963,
                49.1088,
                "420000",
                "+78432000000",
                null));

        var service = await ReadRowAsync(app.Client, HAIR_SERVICES, createdService.Id);
        Assert.Equal("Catalogue Fade", service.GetProperty("name").GetString());
        Assert.Equal("Beard", service.GetProperty("category").GetString());
        Assert.Equal(45, service.GetProperty("defaultDurationMinutes").GetInt32());
        Assert.Equal("Seeded", service.GetProperty("description").GetString());
        Assert.True(service.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, service.GetProperty("usageCount").GetInt32());

        var specialization = await ReadRowAsync(
            app.Client,
            SPECIALIZATIONS,
            createdSpecialization.Id);
        Assert.Equal("Catalogue Skin Fade", specialization.GetProperty("name").GetString());
        Assert.True(specialization.GetProperty("isActive").GetBoolean());

        var salon = await ReadRowAsync(app.Client, SALONS, createdSalon.Id);
        Assert.Equal("Catalogue Salon", salon.GetProperty("name").GetString());
        Assert.Equal("Казань", salon.GetProperty("city").GetString());
        Assert.Equal("ул. Профсоюзная, 1", salon.GetProperty("address").GetString());
        Assert.Equal("Europe/Moscow", salon.GetProperty("timeZoneId").GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Hair_service_rename_keeps_the_category()
    {
        await using var app = await TestApp.StartAsync(_database);
        await SignInAsAdminAsync(app);

        var created = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/hair-services",
            new CreateHairServiceRequest("Original Fade", ServiceCategory.Beard, 30, "Old"));
        var id = created.Id;

        using (var renamed = await app.Client.PutAsJsonAsync(
                   $"api/hair-services/{id}",
                   new UpdateHairServiceRequest("Renamed Fade", "New description", 60)))
        {
            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        }

        var row = await ReadRowAsync(app.Client, HAIR_SERVICES, id);
        Assert.Equal("Renamed Fade", row.GetProperty("name").GetString());
        Assert.Equal("New description", row.GetProperty("description").GetString());
        Assert.Equal(60, row.GetProperty("defaultDurationMinutes").GetInt32());
        // Категория не меняется после создания: иначе предложения барберов потеряли бы смысл.
        Assert.Equal("Beard", row.GetProperty("category").GetString());

        // Границы длительности проверяет домен: 0 и 1441 — невалидные значения.
        foreach (var duration in new[] { 0, 1441 })
        {
            using var rejected = await app.Client.PutAsJsonAsync(
                $"api/hair-services/{id}",
                new UpdateHairServiceRequest("Renamed Fade", "New description", duration));

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        // Неудачные правки не испортили строку.
        Assert.Equal(60, (await ReadRowAsync(app.Client, HAIR_SERVICES, id))
            .GetProperty("defaultDurationMinutes").GetInt32());
    }

    [Fact(Timeout = 120_000)]
    public async Task Duplicate_specialization_name_is_conflict()
    {
        await using var app = await TestApp.StartAsync(_database);
        await SignInAsAdminAsync(app);

        await CreateAsync<CreatedResourceResponse>(
            app,
            "api/specializations",
            new CreateSpecializationRequest("Catalogue Fade", "First"));

        using var duplicate = await app.Client.PostAsJsonAsync(
            "api/specializations",
            new CreateSpecializationRequest("catalogue fade", "Second"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "specialization.create.nameTaken",
            await duplicate.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Renaming_specialization_to_taken_name_is_conflict()
    {
        await using var app = await TestApp.StartAsync(_database);
        await SignInAsAdminAsync(app);

        var first = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/specializations",
            new CreateSpecializationRequest("Catalogue First", null));
        var second = await CreateAsync<CreatedResourceResponse>(
            app,
            "api/specializations",
            new CreateSpecializationRequest("Catalogue Second", null));

        using var duplicate = await app.Client.PutAsJsonAsync(
            $"api/specializations/{second.Id}",
            new RenameSpecializationRequest("CATALOGUE FIRST", null));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "specialization.rename.nameTaken",
            await duplicate.Content.ReadAsStringAsync());

        // Переименование не применилось: имя осталось прежним.
        var row = await ReadRowAsync(app.Client, SPECIALIZATIONS, second.Id);
        Assert.Equal("Catalogue Second", row.GetProperty("name").GetString());
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact(Timeout = 120_000)]
    public async Task Salon_unknown_time_zone_is_validation_error()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var salon = await data.CreateSalonAsync("Time Zone Salon");
        await SignInAsAdminAsync(app);

        using var response = await app.Client.PutAsJsonAsync(
            $"api/salons/{salon.Id}/location",
            new UpdateSalonLocationRequest(
                "Teststrasse 1",
                "Berlin",
                "Germany",
                "Mars/Olympus",
                52.5200,
                13.4050,
                null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("salon.timeZoneId.unknown", await response.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Non_admin_cannot_write_the_catalogue()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var service = await data.CreateHairServiceAsync("Forbidden Service");
        var specialization = await data.CreateSpecializationAsync("Forbidden Specialization");
        var salon = await data.CreateSalonAsync("Forbidden Salon");

        var customer = await app.CreateUserAsync(
            $"catalogue-write-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        var responses = new List<HttpResponseMessage>
        {
            await app.Client.PostAsJsonAsync(
                "api/hair-services",
                new CreateHairServiceRequest("Forbidden", ServiceCategory.Haircut, 30, null)),
            await app.Client.PutAsJsonAsync(
                $"api/hair-services/{service.Id}",
                new UpdateHairServiceRequest("Forbidden", null, 30)),
            await app.Client.PostAsync($"api/hair-services/{service.Id}/activation", null),
            await app.Client.DeleteAsync($"api/hair-services/{service.Id}/activation"),
            await app.Client.PostAsJsonAsync(
                "api/specializations",
                new CreateSpecializationRequest("Forbidden", null)),
            await app.Client.PutAsJsonAsync(
                $"api/specializations/{specialization.Id}",
                new RenameSpecializationRequest("Forbidden", null)),
            await app.Client.PostAsync($"api/specializations/{specialization.Id}/activation", null),
            await app.Client.DeleteAsync($"api/specializations/{specialization.Id}/activation"),
            await app.Client.PostAsJsonAsync(
                "api/salons",
                new CreateSalonRequest(
                    "Forbidden",
                    "Teststrasse 1",
                    "Berlin",
                    "Germany",
                    "Europe/Berlin",
                    52.5200,
                    13.4050,
                    null,
                    null,
                    null)),
            await app.Client.PutAsJsonAsync(
                $"api/salons/{salon.Id}/location",
                new UpdateSalonLocationRequest(
                    "Teststrasse 1",
                    "Berlin",
                    "Germany",
                    "Europe/Berlin",
                    52.5200,
                    13.4050,
                    null)),
            await app.Client.PutAsJsonAsync(
                $"api/salons/{salon.Id}/contact-info",
                new UpdateSalonContactInfoRequest("+4930123456", null)),
            await app.Client.PostAsync($"api/salons/{salon.Id}/activation", null),
            await app.Client.DeleteAsync($"api/salons/{salon.Id}/activation"),
        };

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Salon_location_and_contact_edits_are_independent()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var salon = await data.CreateSalonAsync(
            "Independent Salon",
            city: "Berlin",
            phoneNumber: "+4930000000",
            websiteUrl: "https://salon.example/");
        await SignInAsAdminAsync(app);

        // Первый запрос меняет только адрес: контакты остаются прежними.
        using (var location = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salon.Id}/location",
                   new UpdateSalonLocationRequest(
                       "Neue Strasse 2",
                       "Potsdam",
                       "Germany",
                       "Europe/Berlin",
                       52.3906,
                       13.0645,
                       "14467")))
        {
            Assert.Equal(HttpStatusCode.NoContent, location.StatusCode);
        }

        var afterLocation = await ReadRowAsync(app.Client, SALONS, salon.Id);
        Assert.Equal("Neue Strasse 2", afterLocation.GetProperty("address").GetString());
        Assert.Equal("Potsdam", afterLocation.GetProperty("city").GetString());
        Assert.Equal("+4930000000", afterLocation.GetProperty("phoneNumber").GetString());
        Assert.Equal(
            "https://salon.example/",
            afterLocation.GetProperty("websiteUrl").GetString());

        // Второй запрос меняет только контакты: адрес не откатывается.
        using (var contacts = await app.Client.PutAsJsonAsync(
                   $"api/salons/{salon.Id}/contact-info",
                   new UpdateSalonContactInfoRequest("+4930999999", null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, contacts.StatusCode);
        }

        var afterContacts = await ReadRowAsync(app.Client, SALONS, salon.Id);
        Assert.Equal("+4930999999", afterContacts.GetProperty("phoneNumber").GetString());
        Assert.Null(afterContacts.GetProperty("websiteUrl").GetString());
        Assert.Equal("Neue Strasse 2", afterContacts.GetProperty("address").GetString());
    }

    private static async Task<TResponse> CreateAsync<TResponse>(
        TestApp app,
        string url,
        object body)
    {
        using var response = await app.Client.PostAsync(
            url,
            JsonContent.Create(body, options: JsonOptions));

        var detail = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"POST {url} returned {(int)response.StatusCode}: {detail}");

        var created = await response.Content.ReadFromJsonAsync<TResponse>();
        Assert.NotNull(created);

        return created;
    }

    private static async Task SignInAsAdminAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"catalogue-write-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);
    }

    private static async Task<JsonElement> ReadRowAsync(
        HttpClient client,
        string url,
        Guid id)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return Assert.Single(
            document.RootElement.EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == id).Clone();
    }
}
