using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Notifications;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет EF-модель напоминаний барберу без подключения к базе: таблицы, колонки,
/// уникальные фильтры индексов и индекс скана подтверждённых броней.
/// </summary>
/// <remarks>
/// Модель строится на неоткрытом контексте, как в <c>RefreshTokenExpiresAtIndexModelTests</c>.
/// Порядок и направление колонок индекса есть только в design-time модели, поэтому
/// используется <see cref="IDesignTimeModel"/>.
/// </remarks>
public class NotificationModelTests
{
    [Fact]
    public void NotificationTableAndColumns_MatchTheContract()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(BarberNotification));

        Assert.NotNull(entity);
        Assert.Equal("barber_notifications", entity.GetTableName());
        Assert.Equal("pk_barber_notifications", entity.GetKeys().Single().GetName());
        Assert.Equal(
            [
                "booking_id", "bookings_count", "created_at", "delivery", "delivery_attempts",
                "digest_local_date", "id", "kind", "last_error", "next_attempt_at", "read_at",
                "salon_name", "salon_time_zone_id", "service_name", "start_at", "updated_at",
                "user_id"
            ],
            entity.GetProperties()
                .Select(property => property.GetColumnName())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void NotificationIndexes_MatchTheSpecification()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(BarberNotification));

        Assert.NotNull(entity);

        var userCreated = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_barber_notifications_user_created");
        Assert.False(userCreated.IsUnique);
        Assert.Equal(
            ["user_id", "created_at"],
            userCreated.Properties.Select(property => property.GetColumnName()).ToArray());
        Assert.Equal([false, true], userCreated.IsDescending);

        var upcoming = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_barber_notifications_upcoming");
        Assert.True(upcoming.IsUnique);
        Assert.Equal("booking_id", Assert.Single(upcoming.Properties).GetColumnName());
        Assert.Equal("kind = 1", upcoming.GetFilter());

        var digest = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_barber_notifications_digest");
        Assert.True(digest.IsUnique);
        Assert.Equal(
            ["user_id", "digest_local_date"],
            digest.Properties.Select(property => property.GetColumnName()).ToArray());
        Assert.Equal("kind = 2", digest.GetFilter());

        var delivery = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ix_barber_notifications_delivery");
        Assert.False(delivery.IsUnique);
        Assert.Equal(
            ["next_attempt_at", "created_at"],
            delivery.Properties.Select(property => property.GetColumnName()).ToArray());
        Assert.Equal("delivery IN (0, 3)", delivery.GetFilter());
    }

    [Fact]
    public void SettingsTable_HasPrimaryKeyCascadeForeignKeyAndTelegramUniqueIndexes()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(BarberNotificationSettings));

        Assert.NotNull(entity);
        Assert.Equal("barber_notification_settings", entity.GetTableName());

        var key = Assert.Single(entity.GetKeys());
        Assert.Equal("pk_barber_notification_settings", key.GetName());
        Assert.Equal("user_id", Assert.Single(key.Properties).GetColumnName());

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal("fk_barber_notification_settings_users_user_id", foreignKey.GetConstraintName());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);

        var chat = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_barber_notification_settings_telegram_chat_id");
        Assert.True(chat.IsUnique);
        Assert.Equal("telegram_chat_id", Assert.Single(chat.Properties).GetColumnName());
        Assert.Equal("telegram_chat_id IS NOT NULL", chat.GetFilter());

        var code = Assert.Single(
            entity.GetIndexes(),
            index => index.GetDatabaseName() == "ux_barber_notification_settings_telegram_link_code_hash");
        Assert.True(code.IsUnique);
        Assert.Equal("telegram_link_code_hash", Assert.Single(code.Properties).GetColumnName());
        Assert.Equal("telegram_link_code_hash IS NOT NULL", code.GetFilter());
    }

    [Fact]
    public void BookingStatusStartAtIndex_IsPartialOnActiveStatuses()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(Booking));

        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_bookings_status_start_at");

        Assert.False(index.IsUnique);
        Assert.Equal(
            ["status", "start_at"],
            index.Properties.Select(property => property.GetColumnName()).ToArray());
        // Значения фильтра — это Pending = 1 и Confirmed = 2 из BookingStatus.
        Assert.Equal("status IN (1, 2)", index.GetFilter());
    }

    [Fact]
    public void TelegramUpdatesTable_IsKeyedByUpdateId()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(TelegramUpdate));

        Assert.NotNull(entity);
        Assert.Equal("telegram_updates", entity.GetTableName());

        var key = Assert.Single(entity.GetKeys());
        Assert.Equal("pk_telegram_updates", key.GetName());
        Assert.Equal("update_id", Assert.Single(key.Properties).GetColumnName());
        Assert.NotNull(entity.FindProperty("ReceivedAt"));
    }

    private static IModel BuildModel()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Port=59999;Database=model_only;Username=test;Password=test")
                .Options);

        return context.GetService<IDesignTimeModel>().Model;
    }
}
