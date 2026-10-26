using System.Net;
using System.Text.Json;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Keyset-пагинация поиска на границе групп рейтинга: порядок
/// <c>(rating IS NULL), rating DESC, id</c>, NULL-рейтинг последним.
/// </summary>
/// <remarks>
/// Самый хрупкий запрос проекта: предикат курсора смешивает два условия (строго меньший
/// рейтинг и равный рейтинг с большим id), а NULL-группа продолжается отдельной веткой.
/// Ошибка в предикате даёт либо дубль, либо пропуск строки на границе страниц — поэтому
/// тест обходит все страницы и сравнивает полную последовательность с ожидаемой.
/// Рейтинг проставляется точечным SQL: публикация отзыва проверяется в других срезах.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class SearchKeysetTests
{
    private readonly PostgresFixture _database;

    public SearchKeysetTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Cursor_walks_rating_groups_without_duplicates_or_gaps()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Две группы по два одинаковых рейтинга и три профиля без рейтинга: при pageSize=2
        // границы страниц проходят и внутри группы, и между группами, и внутри NULL-группы.
        decimal?[] ratings = [4.50m, 4.50m, 3.00m, 3.00m, null, null, null];
        var expected = new List<(Guid Id, decimal? Rating)>();

        for (var index = 0; index < ratings.Length; index++)
        {
            var profile = await data.CreateBarberProfileAsync($"Search Barber {index + 1}");

            if (ratings[index] is { } rating)
            {
                await data.SetRatingAsync(profile.Id, rating, reviewsCount: index + 1);
            }

            expected.Add((profile.Id, ratings[index]));
        }

        // PostgreSQL сравнивает uuid побайтово; каноническая строка даёт тот же порядок.
        expected = expected
            .OrderBy(item => item.Rating is null)
            .ThenByDescending(item => item.Rating)
            .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

        var pages = new List<SearchPage>();
        string? cursor = null;

        for (var guard = 0; guard < 20; guard++)
        {
            var url = "api/barbers?pageSize=2";
            if (cursor is not null)
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await ReadPageAsync(app.Client, url);
            pages.Add(page);

            if (page.NextCursor is null)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        var actual = pages.SelectMany(page => page.Items).ToList();

        Assert.Equal(4, pages.Count);
        Assert.True(pages[^1].NextCursor is null, "Последняя страница обязана не иметь nextCursor.");
        Assert.All(pages.Take(pages.Count - 1), page => Assert.Equal(2, page.Items.Count));

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Count, actual.Select(item => item.Id).Distinct().Count());
        Assert.Equal(expected.Select(item => item.Id), actual.Select(item => item.Id));
        Assert.Equal(expected.Select(item => item.Rating), actual.Select(item => item.Rating));

        // Рейтинги не возрастают, NULL идут строго после всех значений.
        var nonNull = actual.TakeWhile(item => item.Rating is not null).Select(item => item.Rating!.Value).ToList();
        Assert.Equal(nonNull.OrderByDescending(value => value), nonNull);
        Assert.All(actual.Skip(nonNull.Count), item => Assert.Null(item.Rating));
    }

    private static async Task<SearchPage> ReadPageAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var items = new List<(Guid Id, decimal? Rating)>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetGuid();
            var ratingElement = item.GetProperty("rating");
            var rating = ratingElement.ValueKind == JsonValueKind.Null
                ? (decimal?)null
                : ratingElement.GetDecimal();

            items.Add((id, rating));
        }

        var nextCursor = root.GetProperty("nextCursor").ValueKind == JsonValueKind.Null
            ? null
            : root.GetProperty("nextCursor").GetString();

        return new SearchPage(items, nextCursor);
    }

    private sealed record SearchPage(IReadOnlyList<(Guid Id, decimal? Rating)> Items, string? NextCursor);
}
