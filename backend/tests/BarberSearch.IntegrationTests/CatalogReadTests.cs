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
            defaultDurationMinutes: 45,
            specializationId: alphaSpecialization.Id);
        var betaHaircut = await data.CreateHairServiceAsync(
            "Beta Haircut",
            ServiceCategory.Haircut,
            defaultDurationMinutes: 30,
            specializationId: alphaSpecialization.Id);
        var beard = await data.CreateHairServiceAsync(
            "Beard Trim",
            ServiceCategory.Beard,
            defaultDurationMinutes: 30,
            specializationId: zetaSpecialization.Id);
        await data.CreateHairServiceAsync(
            "Hidden Haircut",
            ServiceCategory.Haircut,
            defaultDurationMinutes: 30,
            isActive: false,
            specializationId: alphaSpecialization.Id);

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
        Assert.Equal(alphaSpecialization.Id, first.GetProperty("specializationId").GetGuid());
        Assert.Equal(45, first.GetProperty("defaultDurationMinutes").GetInt32());
        Assert.Equal("About Alpha Haircut", first.GetProperty("description").GetString());

        // Двухшаговый выбор витрины: фильтр по направлению отдаёт только его услуги.
        using var beardServices = JsonDocument.Parse(
            await ReadBodyAsync(
                app.Client,
                $"api/hair-services?specializationId={zetaSpecialization.Id}"));
        Assert.Equal(
            new[] { beard.Id },
            beardServices.RootElement.EnumerateArray()
                .Select(row => row.GetProperty("id").GetGuid()));
    }

    [Fact(Timeout = 180_000)]
    public async Task Specialization_category_is_derived_from_active_services()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var faded = await data.CreateSpecializationAsync("Alpha Fades");
        var blond = await data.CreateSpecializationAsync("Beta Blond");
        var tie = await data.CreateSpecializationAsync("Gamma Tie");
        var idle = await data.CreateSpecializationAsync("Delta Idle");

        // Большинство: 2 × Coloring + 1 × HairTreatment → Coloring.
        await data.CreateHairServiceAsync(
            "Blond One",
            ServiceCategory.Coloring,
            specializationId: blond.Id);
        await data.CreateHairServiceAsync(
            "Blond Two",
            ServiceCategory.Coloring,
            specializationId: blond.Id);
        await data.CreateHairServiceAsync(
            "Blond Care",
            ServiceCategory.HairTreatment,
            specializationId: blond.Id);

        // Выключенная услуга не участвует: 2 × Haircut активны, 1 × Coloring выключена.
        await data.CreateHairServiceAsync(
            "Fade One",
            ServiceCategory.Haircut,
            specializationId: faded.Id);
        await data.CreateHairServiceAsync(
            "Fade Two",
            ServiceCategory.Haircut,
            specializationId: faded.Id);
        await data.CreateHairServiceAsync(
            "Fade Tint",
            ServiceCategory.Coloring,
            isActive: false,
            specializationId: faded.Id);

        // Ничья: 1 × Coloring + 1 × Haircut → меньший код, Haircut.
        await data.CreateHairServiceAsync(
            "Tie Color",
            ServiceCategory.Coloring,
            specializationId: tie.Id);
        await data.CreateHairServiceAsync(
            "Tie Cut",
            ServiceCategory.Haircut,
            specializationId: tie.Id);

        using var document = JsonDocument.Parse(
            await ReadBodyAsync(app.Client, "api/specializations"));
        var rows = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(4, rows.Count);
        Assert.All(
            rows,
            row => Assert.Equal(JsonValueKind.String, row.GetProperty("category").ValueKind));

        var categories = rows.ToDictionary(
            row => row.GetProperty("id").GetGuid(),
            row => row.GetProperty("category").GetString());

        Assert.Equal("Coloring", categories[blond.Id]);
        Assert.Equal("Haircut", categories[faded.Id]);
        Assert.Equal("Haircut", categories[tie.Id]);
        Assert.Equal("Other", categories[idle.Id]);
    }

    private static async Task<string> ReadBodyAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
