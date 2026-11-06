using System.Net;
using System.Text.Json;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.HairServices;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Фильтры поиска: язык и подстрока дополнительной услуги, а также сочетания с городом,
/// специализацией и услугой.
/// </summary>
/// <remarks>
/// Язык — один, передаётся именем enum без учёта регистра; неизвестное значение даёт
/// <c>400</c> с проектным кодом, а пустое — отсутствие фильтра. Дополнительные услуги —
/// свободные строки, поэтому фильтр ищет подстроку без учёта регистра в любом элементе
/// списка и не трактует <c>%</c> как шаблон. Комбинации проверяются
/// на одном профиле с реальными связями: город, специализация и услуга строятся доменными
/// фабриками, а не SQL-вставками.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SearchFiltersTests
{
    private static readonly string[] ExpectedAdditionalServices =
        ["Бритьё головы", "Камуфляж седины", "Уход за бородой"];

    private readonly PostgresFixture _database;

    public SearchFiltersTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Language_filter_is_case_insensitive_and_rejects_unknown_values()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var russian = await data.CreateBarberProfileAsync(
            "Russian Barber",
            languages: [Language.Ru]);
        var english = await data.CreateBarberProfileAsync(
            "English Barber",
            languages: [Language.En, Language.De]);

        Assert.Equal(
            new[] { russian.Id },
            await ReadIdsAsync(app.Client, "api/barbers?language=Ru"));
        Assert.Equal(
            new[] { russian.Id },
            await ReadIdsAsync(app.Client, "api/barbers?language=ru"));
        Assert.Empty(await ReadIdsAsync(app.Client, "api/barbers?language=Uz"));
        Assert.Equal(
            new[] { russian.Id, english.Id },
            await ReadIdsAsync(app.Client, "api/barbers?language="));

        await AssertInvalidLanguageAsync(app.Client, "api/barbers?language=xx");
        await AssertInvalidLanguageAsync(app.Client, "api/barbers?language=1");
    }

    [Fact(Timeout = 180_000)]
    public async Task Additional_service_filter_matches_any_substring_case_insensitively()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var beard = await data.CreateBarberProfileAsync(
            "Beard Barber",
            additionalServices: ["Оформление бороды", "Бритьё головы"]);
        var head = await data.CreateBarberProfileAsync(
            "Head Barber",
            additionalServices: ["Бритьё головы"]);
        var plain = await data.CreateBarberProfileAsync("Plain Barber");

        Assert.Equal(
            new[] { beard.Id },
            await ReadIdsAsync(app.Client, AdditionalServiceUrl("бород")));
        Assert.Equal(
            new[] { beard.Id },
            await ReadIdsAsync(app.Client, AdditionalServiceUrl("БОРОД")));
        Assert.Equal(
            new[] { beard.Id, head.Id },
            await ReadIdsAsync(app.Client, AdditionalServiceUrl("БРИТЬЁ")));
        Assert.Empty(
            await ReadIdsAsync(app.Client, AdditionalServiceUrl("массаж")));

        // `%` — литерал, а не шаблон: без экранирования фильтр нашёл бы все профили.
        Assert.Empty(
            await ReadIdsAsync(app.Client, AdditionalServiceUrl("100%")));

        Assert.Equal(
            new[] { beard.Id, head.Id, plain.Id },
            await ReadIdsAsync(app.Client, "api/barbers?additionalService="));
    }

    [Fact(Timeout = 180_000)]
    public async Task Additional_services_endpoint_returns_distinct_values()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        await data.CreateBarberProfileAsync(
            "First Barber",
            additionalServices: ["Уход за бородой", "Бритьё головы"]);
        await data.CreateBarberProfileAsync(
            "Second Barber",
            additionalServices: ["Бритьё головы", "Камуфляж седины"]);
        await data.CreateBarberProfileAsync("Third Barber");

        using var response = await app.Client.GetAsync("api/barbers/additional-services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var values = JsonSerializer.Deserialize<List<string>>(body);

        Assert.NotNull(values);
        Assert.Equal(ExpectedAdditionalServices, values);
    }

    [Fact(Timeout = 180_000)]
    public async Task Language_filter_combines_with_city_specialization_and_service()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var profile = await data.CreateBarberProfileAsync(
            "Combined Barber",
            languages: [Language.Ru, Language.En]);
        await data.AttachSalonAsync(profile.Id, "Москва");

        var specialization = await data.CreateSpecializationAsync("Комбо-специализация");
        await data.AddSpecializationAsync(profile.Id, specialization.Id);

        var hairService = await data.CreateHairServiceAsync(
            "Комбо-услуга",
            ServiceCategory.Beard,
            defaultDurationMinutes: 30);
        await data.AttachHairServiceAsync(profile.Id, hairService.Id);

        var moscow = Uri.EscapeDataString("Москва");
        var berlin = Uri.EscapeDataString("Берлин");

        Assert.Equal(
            new[] { profile.Id },
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?city={moscow}&language=Ru"));
        Assert.Empty(
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?city={berlin}&language=Ru"));
        Assert.Equal(
            new[] { profile.Id },
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?hairServiceId={hairService.Id}"));
        Assert.Equal(
            new[] { profile.Id },
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?specializationId={specialization.Id}&language=Ru"));
        Assert.Empty(
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?specializationId={Guid.NewGuid()}"));
        Assert.Equal(
            new[] { profile.Id },
            await ReadIdsAsync(
                app.Client,
                $"api/barbers?city={moscow}&language=Ru"
                + $"&specializationId={specialization.Id}"
                + $"&hairServiceId={hairService.Id}"));
    }

    private static string AdditionalServiceUrl(string term)
        => $"api/barbers?additionalService={Uri.EscapeDataString(term)}";

    private static async Task<List<Guid>> ReadIdsAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task AssertInvalidLanguageAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(
            "barberSearch.language.invalid",
            body,
            StringComparison.Ordinal);
    }
}
