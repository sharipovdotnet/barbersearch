using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Админская проекция отзыва должна собираться одним SQL-запросом с join'ами по агрегатам.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql,
/// в том числе доступ к owned-типу <c>DisplayName</c> и concat имени клиента. База не нужна.
/// </remarks>
public class AdminReviewQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAdminReviewsQuery_JoinsReviewContextIntoSingleSelect()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);

        var sql = ReviewRepository.BuildAdminReviewsQuery(context).ToQueryString();

        // Корень запроса — reviews; контекст визита, барбер, клиент, услуга и салон
        // присоединяются теми же идентификаторами, что лежат в отзыве и брони.
        Assert.Contains("from reviews", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var table in new[]
                 {
                     "bookings",
                     "users",
                     "barber_profiles",
                     "barber_services",
                     "hair_services",
                     "salons"
                 })
        {
            Assert.Contains($"join {table}", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("display_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time_zone_id", sql, StringComparison.OrdinalIgnoreCase);

        // Базовая проекция сортирует по умолчанию, но не ограничивает страницу.
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminReviewsQuery_WithFilterAndOrder_IsTranslatable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);

        var sql = ReviewRepository.BuildAdminReviewsQuery(
                context,
                isPublished: false,
                limit: 3)
            .ToQueryString();

        Assert.Contains("is_published", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
