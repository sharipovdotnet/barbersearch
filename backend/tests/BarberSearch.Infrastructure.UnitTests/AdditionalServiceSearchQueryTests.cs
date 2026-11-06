using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Фильтр по дополнительным услугам и подсказки для него обязаны транслироваться в SQL.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Подстрока должна стать <c>EXISTS (SELECT 1 FROM unnest(additional_services) … ILIKE …)</c>,
/// а не клиентской фильтрацией: иначе keyset-пагинация вернула бы неполную выдачу.
/// Подсказки — <c>unnest</c> + <c>DISTINCT</c> + <c>LIMIT</c>, база не нужна.
/// </remarks>
public class AdditionalServiceSearchQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void AdditionalServiceFilter_UsesUnnestIlikeAndEscape()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: null,
            Language: null,
            AdditionalService: "  бород  ",
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        Assert.Contains("additional_services", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unnest", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_verified", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdditionalServiceFilter_WithoutValue_DoesNotTouchTheCollection()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: null,
            Language: null,
            AdditionalService: "   ",
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        Assert.DoesNotContain("unnest", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdditionalServicesSuggestions_UseUnnestDistinctAndLimit()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildAdditionalServicesQuery(context.BarberProfiles.AsNoTracking())
            .ToQueryString();

        Assert.Contains("additional_services", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unnest", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options);
}
