using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг индекса <c>expires_at</c> фоновой уборки refresh-токенов (R-50) — без
/// подключения к базе.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>ReviewModerationIndexModelTests</c>: модель строится на неоткрытом
/// контексте, проверяются имя индекса, уникальность и колонка. Порядок свойств есть только
/// в design-time модели, поэтому используется <see cref="IDesignTimeModel"/>.
/// </remarks>
public class RefreshTokenExpiresAtIndexModelTests
{
    [Fact]
    public void RefreshTokenExpiresAtIndex_IsNonUniqueOnExpiresAt()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RefreshToken));
        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_refresh_tokens_expires_at");

        Assert.False(index.IsUnique);

        // Один столбец: уборка фильтрует только по expires_at, поэтому составной индекс
        // (и тем более уникальный) не нужен.
        Assert.Collection(
            index.Properties,
            property =>
            {
                Assert.Equal("ExpiresAt", property.Name);
                Assert.Equal("expires_at", property.GetColumnName());
            });
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
