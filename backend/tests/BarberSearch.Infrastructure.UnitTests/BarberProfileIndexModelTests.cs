using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет, что в EF-модели <c>barber_profiles</c> не осталось удалённых индексов (R-38).
/// </summary>
/// <remarks>
/// На таблице было три пересекающихся индекса. Поиск (<c>SearchAsync</c>) обслуживает только
/// <c>ix_barber_profiles_search_keyset</c>; <c>ix_barber_profiles_is_active_is_verified_rating</c>
/// удалён миграцией <c>DropRedundantBarberProfileIndexes</c>, а <c>ix_barber_profiles_search</c>
/// удалён её raw SQL. Тест без подключения к базе фиксирует, что составной индекс не вернётся
/// в модель (и, значит, в snapshot и следующую миграцию).
/// </remarks>
public class BarberProfileIndexModelTests
{
    private static readonly string[] RemovedIndexColumns = ["IsActive", "IsVerified", "Rating"];

    [Fact]
    public void BarberProfiles_DoNotDeclareRemovedSearchIndexes()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(BarberProfile));
        Assert.NotNull(entity);

        Assert.DoesNotContain(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_barber_profiles_is_active_is_verified_rating");

        // Raw SQL-индекс AddReadIndexes в модели не объявлялся, но проверяем и имя:
        // если кто-то вернёт его через HasIndex, тест это поймает.
        Assert.DoesNotContain(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_barber_profiles_search");

        // Та же проверка по колонкам, а не по имени: переименованный дубликат тоже дефект.
        Assert.DoesNotContain(
            entity.GetIndexes(),
            index => index.Properties
                .Select(property => property.Name)
                .SequenceEqual(RemovedIndexColumns));

        // Остальные индексы таблицы не тронуты: уникальность user_id и GIN по языкам.
        Assert.Contains(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_barber_profiles_user_id" && index.IsUnique);
        Assert.Contains(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_barber_profiles_languages");
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
