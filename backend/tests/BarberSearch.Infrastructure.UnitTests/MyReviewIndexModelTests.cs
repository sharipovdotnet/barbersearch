using BarberSearch.Domain.Reviews;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг индекса списка «мои отзывы».
/// </summary>
/// <remarks>
/// Стиль повторяет <c>AdminReviewIndexModelTests</c>: модель строится на неоткрытом
/// контексте, проверяются имя индекса, колонки, порядок сортировки и отсутствие фильтра.
/// </remarks>
public class MyReviewIndexModelTests
{
    private static readonly bool[] ExpectedDescending = [false, true, false];

    [Fact]
    public void MyReviewsIndex_IsFullDescendingOnCreatedAtWithIdTieBreak()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Review));
        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_reviews_user_id_created_at_id");

        Assert.False(index.IsUnique);

        Assert.Collection(
            index.Properties,
            property =>
            {
                Assert.Equal("UserId", property.Name);
                Assert.Equal("user_id", property.GetColumnName());
            },
            property =>
            {
                Assert.Equal("CreatedAt", property.Name);
                Assert.Equal("created_at", property.GetColumnName());
            },
            property =>
            {
                Assert.Equal("Id", property.Name);
                Assert.Equal("id", property.GetColumnName());
            });

        // Список отдаёт оба статуса, поэтому индекс полный, а не частичный.
        Assert.Null(index.GetFilter());

        // ORDER BY created_at DESC, id ASC.
        Assert.NotNull(index.IsDescending);
        Assert.Equal(ExpectedDescending, index.IsDescending);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
