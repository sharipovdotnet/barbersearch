using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проекция «мои отзывы» должна собираться одним SQL-запросом с контекстом визита и без автора.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql,
/// включая owned-тип <c>DisplayName</c> и keyset-условие по <c>Guid.CompareTo</c>.
/// </remarks>
public class MyReviewQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildMyReviewsQuery_JoinsVisitContextAndFiltersByAuthor()
    {
        using var context = CreateContext();

        var sql = ReviewRepository.BuildMyReviewsQuery(context, Guid.CreateVersion7())
            .ToQueryString();

        // Корень запроса — reviews; контекст визита присоединяется теми же идентификаторами.
        Assert.Contains("from reviews", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var table in new[]
                 {
                     "bookings",
                     "barber_profiles",
                     "barber_services",
                     "hair_services",
                     "salons"
                 })
        {
            Assert.Contains($"join {table}", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("display_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time_zone_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_id", sql, StringComparison.OrdinalIgnoreCase);

        // Автор в ответ не входит: это сам вызывающий.
        Assert.DoesNotContain("join users", sql, StringComparison.OrdinalIgnoreCase);

        // Свежие сверху; базовая проекция страницу не ограничивает.
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMyReviewsQuery_WithCursorAndLimit_IsTranslatable()
    {
        using var context = CreateContext();
        var position = new BarberSearch.Application.Common.ReviewCursor(
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7());

        var sql = ReviewRepository.BuildMyReviewsQuery(
                context,
                Guid.CreateVersion7(),
                cursor: position,
                limit: 3)
            .ToQueryString();

        // Keyset-условие то же, что у публичного списка: created_at < cursor OR (=, id >).
        Assert.Contains("created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options);
}
