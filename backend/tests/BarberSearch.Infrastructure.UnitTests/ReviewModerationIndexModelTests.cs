using BarberSearch.Domain.Reviews;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг частичного индекса очереди модерации (R-35) — без подключения к базе.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>AuditLogModelTests</c>: модель строится на неоткрытом контексте,
/// проверяются имя индекса, колонки, фильтр и порядок сортировки. Порядок колонок есть только
/// в design-time модели, поэтому используется <see cref="IDesignTimeModel"/>.
/// </remarks>
public class ReviewModerationIndexModelTests
{
    [Fact]
    public void ReviewModerationIndex_IsPartialAscendingOnCreatedAtAndId()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Review));
        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_reviews_moderation_created_at_id");

        Assert.False(index.IsUnique);

        Assert.Collection(
            index.Properties,
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

        // Очередь читает только скрытые отзывы: опубликованные в индекс не попадают.
        Assert.Equal("is_published = false", index.GetFilter());

        // HasIndex без IsDescending — ASC по обеим колонкам, под ORDER BY created_at, id.
        Assert.True(
            index.IsDescending is null || index.IsDescending.All(descending => !descending),
            "The moderation index must be ascending.");
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
