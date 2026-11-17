using BarberSearch.Domain.Push;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет EF-модель push-уведомлений без подключения к базе: таблицы, колонки,
/// check-ограничения, уникальные индексы и индекс доставки.
/// </summary>
/// <remarks>
/// Модель строится на неоткрытом контексте, проверяется design-time модель: фильтры индексов
/// и порядок колонок есть только в ней. Применение миграции на реальной PostgreSQL проверяют
/// интеграционные тесты.
/// </remarks>
public class PushModelTests
{
    [Fact]
    public void DeviceTokenTableAndColumns_MatchTheContract()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(PushDeviceToken));

        Assert.NotNull(entity);
        Assert.Equal("push_device_tokens", entity.GetTableName());
        Assert.Equal("pk_push_device_tokens", entity.GetKeys().Single().GetName());
        Assert.Equal(
            [
                "created_at", "id", "last_seen_at", "platform", "token", "updated_at", "user_id"
            ],
            entity.GetProperties()
                .Select(property => property.GetColumnName())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void DeviceTokenChecks_ForeignKeyAndIndexes_ArePresent()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(PushDeviceToken));

        Assert.NotNull(entity);

        Assert.Equal(
            "platform IN (1, 2)",
            Assert.Single(
                entity.GetCheckConstraints(),
                check => check.Name == "ck_push_device_tokens_platform").Sql);
        Assert.Equal(
            "char_length(token) BETWEEN 1 AND 512",
            Assert.Single(
                entity.GetCheckConstraints(),
                check => check.Name == "ck_push_device_tokens_token_length").Sql);

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal("fk_push_device_tokens_users_user_id", foreignKey.GetConstraintName());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);

        var token = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_push_device_tokens_token");
        Assert.True(token.IsUnique);
        Assert.Equal("token", Assert.Single(token.Properties).GetColumnName());
        Assert.Null(token.GetFilter());

        var user = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_push_device_tokens_user_id");
        Assert.False(user.IsUnique);
        Assert.Equal("user_id", Assert.Single(user.Properties).GetColumnName());
    }

    [Fact]
    public void NotificationTableAndColumns_MatchTheContract()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(PushNotification));

        Assert.NotNull(entity);
        Assert.Equal("push_notifications", entity.GetTableName());
        Assert.Equal("pk_push_notifications", entity.GetKeys().Single().GetName());
        Assert.Equal(
            [
                "barber_name", "booking_id", "created_at", "delivery", "delivery_attempts",
                "id", "kind", "last_error", "next_attempt_at", "salon_name", "salon_time_zone_id",
                "service_name", "start_at", "updated_at", "user_id"
            ],
            entity.GetProperties()
                .Select(property => property.GetColumnName())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        // Вид уведомления хранится как smallint: по нему стоят check и составной индекс.
        var kind = entity.FindProperty(nameof(PushNotification.Kind));
        Assert.NotNull(kind);
        Assert.Equal("kind", kind.GetColumnName());
        Assert.Equal(typeof(PushNotificationKind), kind.ClrType);
        Assert.Equal("smallint", kind.GetColumnType());
    }

    [Fact]
    public void NotificationChecksAndIndexes_ArePresent()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(PushNotification));

        Assert.NotNull(entity);

        Assert.Equal(
            "kind IN (1, 2, 3)",
            Assert.Single(
                entity.GetCheckConstraints(),
                check => check.Name == "ck_push_notifications_kind").Sql);
        Assert.Equal(
            "delivery IN (0, 1, 2, 3)",
            Assert.Single(
                entity.GetCheckConstraints(),
                check => check.Name == "ck_push_notifications_delivery").Sql);
        Assert.Equal(
            "delivery_attempts BETWEEN 0 AND 5",
            Assert.Single(
                entity.GetCheckConstraints(),
                check => check.Name == "ck_push_notifications_delivery_attempts").Sql);

        var recipientForeignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal("fk_push_notifications_users_user_id", recipientForeignKey.GetConstraintName());
        Assert.Equal(DeleteBehavior.Cascade, recipientForeignKey.DeleteBehavior);
        Assert.Equal(
            "user_id",
            Assert.Single(recipientForeignKey.Properties).GetColumnName());

        var booking = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_push_notifications_booking_id");
        Assert.True(booking.IsUnique);
        Assert.Equal(
            ["booking_id", "kind"],
            booking.Properties.Select(property => property.GetColumnName()).ToArray());

        // Старого индекса «только по booking_id» больше нет: иначе событие создания
        // вытесняло бы напоминание той же брони.
        Assert.DoesNotContain(
            entity.GetIndexes(),
            index => index.Properties.Count == 1
                     && index.Properties[0].GetColumnName() == "booking_id");

        var dispatch = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_push_notifications_dispatch");
        Assert.False(dispatch.IsUnique);
        Assert.Equal("created_at", Assert.Single(dispatch.Properties).GetColumnName());
        Assert.Equal("delivery = 0 AND delivery_attempts < 5", dispatch.GetFilter());

        var user = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_push_notifications_user_id");
        Assert.False(user.IsUnique);
        Assert.Equal("user_id", Assert.Single(user.Properties).GetColumnName());
    }

    [Fact]
    public void Migration_AddPushNotificationsExistsAndSnapshotMatchesModel()
    {
        using var context = CreateContext();

        Assert.Contains(
            "20261115154114_AddPushNotifications",
            context.Database.GetMigrations());
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddPushNotificationKind", StringComparison.Ordinal));

        // Снимок модели обновлён вместе с миграцией: следующая миграция не должна
        // неожиданно «находить» незакоммиченные изменения.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static IModel BuildModel()
    {
        using var context = CreateContext();

        return context.GetService<IDesignTimeModel>().Model;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
