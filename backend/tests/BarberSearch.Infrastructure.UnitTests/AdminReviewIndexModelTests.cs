using BarberSearch.Domain.Reviews;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг частичного индекса админского списка опубликованных отзывов.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>ReviewModerationIndexModelTests</c>: модель строится на неоткрытом
/// контексте, проверяются имя индекса, колонки, фильтр и порядок сортировки.
/// </remarks>
public class AdminReviewIndexModelTests
{
    private static readonly bool[] ExpectedDescending = [false, true, false];

    [Fact]
    public void PublishedReviewIndex_IsPartialDescendingOnCreatedAtAndId()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Review));
        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_reviews_published_created_at_id");

        Assert.False(index.IsUnique);

        Assert.Collection(
            index.Properties,
            property =>
            {
                Assert.Equal("IsPublished", property.Name);
                Assert.Equal("is_published", property.GetColumnName());
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

        // Админский список опубликованных читает только опубликованные строки.
        Assert.Equal("is_published = true", index.GetFilter());

        // ORDER BY created_at DESC, id ASC.
        Assert.NotNull(index.IsDescending);
        Assert.Equal(ExpectedDescending, index.IsDescending);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
