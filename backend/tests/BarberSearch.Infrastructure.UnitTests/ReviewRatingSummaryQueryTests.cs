using System.Text.RegularExpressions;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Сводка рейтинга отзывов читается одним агрегирующим SQL-запросом (R-19).
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: тест проверяет именно трансляцию
/// EF Core / Npgsql, поэтому база данных не нужна. Поведение на пустом и непустом
/// наборе проверяется в <c>BarberSearch.Application.UnitTests</c>.
/// </remarks>
public class ReviewRatingSummaryQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildRatingSummaryQuery_TranslatesToSingleAggregateSelect()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);
        var barberProfileId = Guid.CreateVersion7();

        var sql = ReviewRepository
            .BuildRatingSummaryQuery(context, barberProfileId)
            .ToQueryString();

        // COUNT и AVG должны быть в одном SELECT: отдельный CountAsync дал бы второй round-trip
        // под блокировкой профиля (R-19).
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avg(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);

        // Агрегируется только опубликованные отзывы нужного барбера; join'ов нет.
        Assert.Contains("is_published", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(sql, @"\bfrom\s+reviews\b", RegexOptions.IgnoreCase));
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}
