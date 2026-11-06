using System.Net;
using System.Text.Json;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Публичный каталог салонов: анонимный доступ, только активные строки, порядок,
/// фильтры <c>city</c>/<c>search</c> и границы длины.
/// </summary>
/// <remarks>
/// Контракт — голый JSON-массив без пагинации: пустой каталог тоже <c>200</c>, но с <c>[]</c>.
/// Клиент тестового хоста не аутентифицирован, поэтому успешный ответ сам доказывает
/// анонимность действия (классового <c>[Authorize]</c> у салонов больше нет).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SalonDirectoryReadTests
{
    private readonly PostgresFixture _database;

    private static readonly string[] DIRECTORY_FIELDS =
        ["id", "name", "city", "address", "timeZoneId", "kind"];

    public SalonDirectoryReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Directory_is_anonymous_and_returns_only_active_salons_in_stable_order()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Пустой каталог — 200 []: экран выбора не должен падать без данных.
        Assert.Equal("[]", await ReadBodyAsync(app.Client, "api/salons"));

        var beta = await data.CreateSalonAsync(
            "Beta Studio",
            city: "Berlin",
            address: "Teststrasse 1",
            timeZoneId: "Europe/Berlin");
        var alpha = await data.CreateSalonAsync(
            "Alpha Studio",
            city: "Berlin",
            address: "Teststrasse 2",
            timeZoneId: "Europe/Berlin");
        var munich = await data.CreateSalonAsync(
            "Gamma Studio",
            city: "Munich",
            address: "Sendlinger Strasse 3",
            timeZoneId: "Europe/Berlin");
        await data.CreateSalonAsync("Hidden Studio", city: "Aachen", isActive: false);

        using var document = JsonDocument.Parse(
            await ReadBodyAsync(app.Client, "api/salons"));
        var rows = document.RootElement.EnumerateArray().ToList();

        // Порядок — город, название, идентификатор; выключенная строка не попадает.
        Assert.Equal(
            new[] { alpha.Id, beta.Id, munich.Id },
            rows.Select(row => row.GetProperty("id").GetGuid()));

        var first = rows[0];
        Assert.Equal("Alpha Studio", first.GetProperty("name").GetString());
        Assert.Equal("Berlin", first.GetProperty("city").GetString());
        Assert.Equal("Teststrasse 2", first.GetProperty("address").GetString());
        Assert.Equal("Europe/Berlin", first.GetProperty("timeZoneId").GetString());
        Assert.Equal("Salon", first.GetProperty("kind").GetString());
        Assert.Equal(
            DIRECTORY_FIELDS,
            first.EnumerateObject().Select(property => property.Name));
    }

    [Fact(Timeout = 180_000)]
    public async Task Directory_filters_by_city_and_search_and_rejects_long_values()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var alphaBerlin = await data.CreateSalonAsync(
            "Alpha Fade",
            city: "Berlin",
            address: "Main Street 1");
        var percentBerlin = await data.CreateSalonAsync(
            "100% Fade",
            city: "Berlin",
            address: "Side Street 2");
        var alphaMunich = await data.CreateSalonAsync(
            "Alpha Other",
            city: "Munich",
            address: "Hauptstrasse 3");

        // Город: точное совпадение после обрезки пробелов. Порядок — по названию:
        // «100% Fade» идёт раньше «Alpha Fade».
        Assert.Equal(
            new[] { percentBerlin.Id, alphaBerlin.Id },
            await ReadIdsAsync(app.Client, "api/salons?city=Berlin"));
        Assert.Equal(
            new[] { percentBerlin.Id, alphaBerlin.Id },
            await ReadIdsAsync(app.Client, "api/salons?city=%20Berlin%20"));

        // Поиск — по названию (без учёта регистра) и по адресу.
        Assert.Equal(
            new[] { alphaBerlin.Id, alphaMunich.Id },
            await ReadIdsAsync(app.Client, "api/salons?search=alpha"));
        Assert.Equal(
            new[] { alphaBerlin.Id },
            await ReadIdsAsync(app.Client, "api/salons?search=main"));

        // `%` — служебный символ шаблона: без экранирования запрос вернул бы все строки.
        Assert.Equal(
            new[] { percentBerlin.Id },
            await ReadIdsAsync(app.Client, "api/salons?search=%25"));

        // Фильтры складываются: город сужает поиск.
        Assert.Equal(
            new[] { alphaBerlin.Id },
            await ReadIdsAsync(app.Client, "api/salons?city=Berlin&search=alpha"));

        // Ничего не найдено — пустой массив, а не 404.
        Assert.Empty(await ReadIdsAsync(app.Client, "api/salons?search=Nothing"));

        using (var longCity = await app.Client.GetAsync(
                   $"api/salons?city={new string('a', 101)}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longCity.StatusCode);
            Assert.Contains(
                "salon.directory.city",
                await ReadErrorCodesAsync(longCity));
        }

        using (var longSearch = await app.Client.GetAsync(
                   $"api/salons?search={new string('a', 101)}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longSearch.StatusCode);
            Assert.Contains(
                "salon.directory.search",
                await ReadErrorCodesAsync(longSearch));
        }
    }

    private static async Task<string> ReadBodyAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<IReadOnlyList<Guid>> ReadIdsAsync(
        HttpClient client,
        string url)
    {
        using var document = JsonDocument.Parse(await ReadBodyAsync(client, url));

        return [.. document.RootElement
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetGuid())];
    }

    private static async Task<IReadOnlyList<string>> ReadErrorCodesAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!document.RootElement.TryGetProperty("errorCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. codes.EnumerateArray()
            .Where(code => code.ValueKind == JsonValueKind.String)
            .Select(code => code.GetString()!)];
    }
}
