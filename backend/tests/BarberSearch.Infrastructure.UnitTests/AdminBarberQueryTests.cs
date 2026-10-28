using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Админские выборки барберов должны собираться одним SQL-запросом: join по владельцу,
/// поиск через <c>ILIKE</c> и счётчики готовности подзапросами.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql,
/// в том числе owned-типы <c>DisplayName</c> и <c>YearsOfExperience</c>, <c>ILIKE</c>
/// с escape-символом и <c>COUNT(DISTINCT …)</c> по дням расписания. База не нужна.
/// </remarks>
public class AdminBarberQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAdminBarbersQuery_JoinsOwnerAndOrdersNewestFirst()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository.BuildAdminBarbersQuery(context).ToQueryString();

        Assert.Contains("from barber_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", sql, StringComparison.OrdinalIgnoreCase);

        // «Все» и «скрытые» читаются свежими сверху, поэтому DESC; лимит не задан.
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminBarbersQuery_WithPendingSearchAndCursor_IsTranslatable()
    {
        using var context = CreateContext();

        var cursor = new AdminBarberCursor(
            new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var sql = BarberProfileRepository.BuildAdminBarbersQuery(
                context,
                AdminBarberStatus.Pending,
                search: "100% Артём_",
                cursor: cursor,
                limit: 21)
            .ToQueryString();

        // Очередь проверки — FIFO: старые сверху, поэтому ASC, а не DESC.
        Assert.Contains("is_verified", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);

        // Поиск регистронезависимый, а служебные символы шаблона экранированы.
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminBarberDetailQuery_CountsReadinessWithSubqueries()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildAdminBarberDetailQuery(context, Guid.CreateVersion7())
            .ToQueryString();

        Assert.Contains("from barber_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join users", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var table in new[]
                 {
                     "barber_services",
                     "barber_salons",
                     "working_schedules",
                     "portfolio_photos"
                 })
        {
            Assert.Contains(table, sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("count", sql, StringComparison.OrdinalIgnoreCase);
        // Дни расписания схлопываются: пять строк по одному дню — это один день недели.
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_email_verified", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
