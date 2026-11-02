using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг таблицы <c>audit_log</c> (R-34) — без подключения к базе.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>ConcurrencyModelTests</c> и <c>RepositoryConstraintMappingTests</c>:
/// модель строится на неоткрытом контексте, проверяются имена таблицы, колонок, индексов
/// и отсутствие внешнего ключа.
/// </remarks>
public class AuditLogModelTests
{
    /// <summary>
    /// Направление сортировки keyset-индекса: <c>occurred_at DESC, id</c>.
    /// </summary>
    private static readonly bool[] KeysetDescending = [true, false];
    [Fact]
    public void AuditLog_MapsToTableWithPrimaryKey()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AuditLogEntry));
        Assert.NotNull(entity);

        Assert.Equal("audit_log", entity.GetTableName());

        var key = entity.FindPrimaryKey();

        Assert.NotNull(key);
        Assert.Equal("pk_audit_log", key.GetName());

        var id = Assert.Single(key.Properties);

        Assert.Equal(nameof(AuditLogEntry.Id), id.Name);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        Assert.Equal("id", id.GetColumnName());
    }

    [Fact]
    public void AuditLog_HasExpectedColumns()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AuditLogEntry));
        Assert.NotNull(entity);

        AssertColumn(entity, "OccurredAt", "occurred_at", "timestamp with time zone", nullable: false);
        AssertColumn(entity, "ActorUserId", "actor_user_id", "uuid", nullable: true);
        AssertColumn(entity, "Action", "action", "character varying(64)", nullable: false, maxLength: 64);
        AssertColumn(entity, "EntityType", "entity_type", "character varying(64)", nullable: false, maxLength: 64);
        AssertColumn(entity, "EntityId", "entity_id", "uuid", nullable: false);

        // payload — jsonb, а не text: по нему можно строить запросы при разборе инцидента.
        AssertColumn(entity, "Payload", "payload", "jsonb", nullable: false);
    }

    [Fact]
    public void AuditLog_HasThreeIndexesIncludingDescendingOccurredAt()
    {
        using var context = CreateContext();

        // Порядок сортировки колонок есть только в design-time модели: runtime-модель
        // read-optimized и намеренно не хранит эту настройку.
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(AuditLogEntry));
        Assert.NotNull(entity);
        var indexes = entity.GetIndexes().ToDictionary(
            index => index.GetDatabaseName() ?? string.Empty,
            StringComparer.Ordinal);

        Assert.Equal(3, indexes.Count);

        Assert.True(indexes.TryGetValue("ix_audit_log_occurred_at_id", out var occurredAt));
        Assert.NotNull(occurredAt);

        // Ключ админской ленты: occurred_at DESC, id. Второй столбец обязателен —
        // строки одного SaveChanges делят occurred_at, и без id keyset теряет строки.
        Assert.Collection(
            occurredAt.Properties,
            property => Assert.Equal("OccurredAt", property.Name),
            property => Assert.Equal("Id", property.Name));
        Assert.Equal(KeysetDescending, occurredAt.IsDescending);

        Assert.True(indexes.TryGetValue("ix_audit_log_entity_type_entity_id", out var byEntity));
        Assert.NotNull(byEntity);

        Assert.Collection(
            byEntity.Properties,
            property => Assert.Equal("EntityType", property.Name),
            property => Assert.Equal("EntityId", property.Name));

        Assert.True(indexes.TryGetValue("ix_audit_log_actor_user_id", out var byActor));
        Assert.NotNull(byActor);

        Assert.Equal("ActorUserId", Assert.Single(byActor.Properties).Name);
    }

    [Fact]
    public void AuditLog_HasNoForeignKeys()
    {
        // FK на users запретил бы удаление пользователя или каскадно удалил историю:
        // аудит должен переживать удаление аккаунта, поэтому actor_user_id — не ссылка.
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AuditLogEntry));
        Assert.NotNull(entity);

        Assert.Empty(entity.GetForeignKeys());
        Assert.DoesNotContain(
            context.Model.GetEntityTypes().SelectMany(type => type.GetForeignKeys()),
            foreignKey => foreignKey.PrincipalEntityType == entity);
    }

    private static void AssertColumn(
        IEntityType entity,
        string propertyName,
        string columnName,
        string storeType,
        bool nullable,
        int? maxLength = null)
    {
        var property = entity.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(columnName, property.GetColumnName());
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);

        if (maxLength is not null)
        {
            Assert.Equal(maxLength, property.GetMaxLength());
        }
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
