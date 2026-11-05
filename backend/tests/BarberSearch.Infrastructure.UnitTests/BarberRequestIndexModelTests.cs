using BarberSearch.Domain.BarberRequests;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг индексов и check-ограничений таблицы <c>barber_requests</c>.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>AdminReviewIndexModelTests</c>: модель строится на неоткрытом контексте,
/// проверяются имя индекса, колонки, фильтр и порядок сортировки — без подключения к базе.
/// </remarks>
public class BarberRequestIndexModelTests
{
    private static readonly bool[] ExpectedDescending = [false, true, true];

    [Fact]
    public void PendingUserIndex_IsPartialUniqueAndMineIndexIsDescending()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(BarberRequest));
        Assert.NotNull(entity);

        var pending = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_barber_requests_pending_user");

        // Одна активная (Pending = 1) заявка на пользователя; после отказа индекс не мешает.
        Assert.True(pending.IsUnique);
        var property = Assert.Single(pending.Properties);
        Assert.Equal("UserId", property.Name);
        Assert.Equal("user_id", property.GetColumnName());
        Assert.Equal("status = 1", pending.GetFilter());

        var mine = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_barber_requests_user_id_created_at");
        Assert.False(mine.IsUnique);
        Assert.Null(mine.GetFilter());

        Assert.Collection(
            mine.Properties,
            item =>
            {
                Assert.Equal("UserId", item.Name);
                Assert.Equal("user_id", item.GetColumnName());
            },
            item =>
            {
                Assert.Equal("CreatedAt", item.Name);
                Assert.Equal("created_at", item.GetColumnName());
            },
            item =>
            {
                Assert.Equal("Id", item.Name);
                Assert.Equal("id", item.GetColumnName());
            });

        // ORDER BY created_at DESC, id DESC; user_id — ведущая колонка фильтра.
        Assert.NotNull(mine.IsDescending);
        Assert.Equal(ExpectedDescending, mine.IsDescending);
    }

    [Fact]
    public void Table_HasExpectedCheckConstraints()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(BarberRequest));
        Assert.NotNull(entity);

        var constraints = entity.GetCheckConstraints()
            .ToDictionary(
                constraint => constraint.Name!,
                constraint => constraint.Sql!);

        Assert.Equal(6, constraints.Count);
        Assert.Equal("status IN (1, 2, 3)", constraints["ck_barber_requests_status"]);
        Assert.Equal(
            "char_length(display_name) BETWEEN 1 AND 120",
            constraints["ck_barber_requests_display_name"]);
        Assert.Equal(
            "years_of_experience BETWEEN 1 AND 70",
            constraints["ck_barber_requests_years_of_experience"]);
        Assert.Equal(
            "char_length(city) BETWEEN 1 AND 100",
            constraints["ck_barber_requests_city"]);
        Assert.Equal(
            "about IS NULL OR char_length(about) BETWEEN 1 AND 2000",
            constraints["ck_barber_requests_about"]);
        Assert.Equal(
            "rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND 500",
            constraints["ck_barber_requests_rejection_reason"]);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
