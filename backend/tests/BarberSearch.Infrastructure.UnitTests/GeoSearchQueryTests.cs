using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Common;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Гео-режим поиска барберов обязан транслироваться в SQL целиком: расстояние считается
/// в базе (<c>sqrt</c>), порядок — по нему, а параметры координат уходят параметрами запроса.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Без этого теста <c>Math.Sqrt</c>, <c>Min</c> по подзапросу салонов или сортировка по
/// спроецированному полю могли бы уехать в клиентскую фильтрацию — а keyset-пагинация
/// на клиентской фильтрации вернула бы неполную страницу.
/// </remarks>
public class GeoSearchQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    private readonly ITestOutputHelper _output;

    public GeoSearchQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GeoFilter_OrdersByDistanceAndComputesSqrtInSql()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = BuildFilter(new SearchLocation(55.7558, 37.6173));

        var sql = repository
            .BuildSearchRowsQuery(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        _output.WriteLine(sql);

        Assert.Contains("sqrt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        // Порядок гео-режима: сначала группа «без расстояния», затем расстояние, затем id.
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        // Параметры координат, а не литералы: запрос кэшируется планом.
        Assert.Contains("@latitude", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@longitude", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@kilometresPerDegreeLongitude", sql, StringComparison.OrdinalIgnoreCase);
        // Расстояние — минимум по активным салонам, те же условия видимости, что у города.
        Assert.Contains("barber_salons", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("salons", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moderation_status", sql, StringComparison.OrdinalIgnoreCase);
        // Публичная видимость и лимит страницы остаются на месте.
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_verified", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        // Рейтинг в гео-порядке не участвует: иначе курсор перестал бы быть двухключевым.
        Assert.DoesNotContain("rating DESC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoFilter_WithCursor_FiltersByDistanceAndId()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = BuildFilter(
            new SearchLocation(55.7558, 37.6173),
            new BarberSearchCursor(4.5m, Guid.CreateVersion7(), 3.25));

        var sql = repository
            .BuildSearchRowsQuery(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        _output.WriteLine(sql);

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        // Курсор гео-режима: «дальше по расстоянию или тот же километраж и больший id».
        // Значение курсора уходит параметром, а не литералом.
        Assert.Contains("@distance", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL OR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoFilter_WithCursorInsideNullGroup_PagesByIdOnly()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = BuildFilter(
            new SearchLocation(55.7558, 37.6173),
            new BarberSearchCursor(null, Guid.CreateVersion7()));

        var sql = repository
            .BuildSearchRowsQuery(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        _output.WriteLine(sql);

        // Группа «без активного салона» идёт последней и продолжается по возрастанию id.
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchWithoutLocation_KeepsTheRatingOrdering()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = BuildFilter(null);

        var sql = repository
            .BuildSearchRowsQuery(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        _output.WriteLine(sql);

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rating", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
        // В обычном режиме расстояние не считается вовсе: ни sqrt, ни салонов в проекции.
        Assert.DoesNotContain("sqrt", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static BarberSearchFilter BuildFilter(
        SearchLocation? location,
        BarberSearchCursor? cursor = null)
        => new(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: null,
            Language: null,
            AdditionalService: null,
            Cursor: cursor,
            Limit: 21,
            Location: location);

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options);
}
