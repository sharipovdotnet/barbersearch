using System.Net;
using System.Text.Json;
using BarberSearch.Domain.HairServices;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Публичные чтения справочников: анонимный доступ, только активные строки, порядок.
/// </summary>
/// <remarks>
/// Контракт — голый JSON-массив без пагинации: пустой справочник тоже <c>200</c>, но с <c>[]</c>.
/// Клиент тестового хоста не аутентифицирован, поэтому успешный ответ сам доказывает
/// анонимность действия (классовый <c>[Authorize]</c> дал бы 401).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class CatalogReadTests
{
    private readonly PostgresFixture _database;

    public CatalogReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Catalogs_are_anonymous_and_return_only_active_rows_in_stable_order()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Пустые справочники — 200 []: витрина не должна падать на пустом каталоге.
        Assert.Equal("[]", await ReadBodyAsync(app.Client, "api/specializations"));
        Assert.Equal("[]", await ReadBodyAsync(app.Client, "api/hair-services"));

        var alphaSpecialization = await data.CreateSpecializationAsync("Alpha Specialization");
        var zetaSpecialization = await data.CreateSpecializationAsync("Zeta Specialization");
        await data.CreateSpecializationAsync("Hidden Specialization", isActive: false);

        var alphaHaircut = await data.CreateHairServiceAsync(
            "Alpha Haircut",
            ServiceCategory.Haircut,
            defaultDurationMinutes: 45);
        var betaHaircut = await data.CreateHairServiceAsync(
            "Beta Haircut",
            ServiceCategory.Haircut,
            defaultDurationMinutes: 30);
        var beard = await data.CreateHairServiceAsync(
            "Beard Trim",
            ServiceCategory.Beard,
            defaultDurationMinutes: 30);
        await data.CreateHairServiceAsync(
            "Hidden Haircut",
            ServiceCategory.Haircut,
            defaultDurationMinutes: 30,
            isActive: false);

        using var specializations = JsonDocument.Parse(
            await ReadBodyAsync(app.Client, "api/specializations"));
        var specializationRows = specializations.RootElement.EnumerateArray().ToList();

        Assert.Equal(
            new[] { alphaSpecialization.Id, zetaSpecialization.Id },
            specializationRows.Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal("Alpha Specialization", specializationRows[0].GetProperty("name").GetString());
        Assert.Equal(
            "About Alpha Specialization",
            specializationRows[0].GetProperty("description").GetString());

        using var hairServices = JsonDocument.Parse(
            await ReadBodyAsync(app.Client, "api/hair-services"));
        var hairServiceRows = hairServices.RootElement.EnumerateArray().ToList();

        // Порядок репозитория: категория (Haircut = 1, Beard = 2), затем имя.
        Assert.Equal(
            new[] { alphaHaircut.Id, betaHaircut.Id, beard.Id },
            hairServiceRows.Select(row => row.GetProperty("id").GetGuid()));

        var first = hairServiceRows[0];
        Assert.Equal("Alpha Haircut", first.GetProperty("name").GetString());
        Assert.Equal("Haircut", first.GetProperty("category").GetString());
        Assert.Equal(45, first.GetProperty("defaultDurationMinutes").GetInt32());
        Assert.Equal("About Alpha Haircut", first.GetProperty("description").GetString());
    }

    private static async Task<string> ReadBodyAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
