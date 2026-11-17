using BarberSearch.Domain;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>push_notifications</c> — очередь push-уведомлений клиенту.
/// </summary>
/// <remarks>
/// <para>
/// Таблица — снимок на момент создания: названия салона, мастера и услуги и пояс салона
/// не пересчитываются, поэтому внешних ключей на каталог нет. Строка переживает
/// переименование услуги и удаление салона; на неё не ссылаются другие таблицы.
/// </para>
/// <para>
/// Уникальный индекс <c>ux_push_notifications_booking_id</c> по паре <c>(booking_id, kind)</c>:
/// на один визит — не больше одного уведомления каждого вида, поэтому событие создания и
/// напоминание сосуществуют, а повторный тик и второй экземпляр приложения дублей не создают
/// (<c>INSERT … ON CONFLICT DO NOTHING</c>).
/// </para>
/// <para>
/// <c>ix_push_notifications_dispatch</c> повторяет фильтр выборки доставки:
/// <c>delivery = Pending AND delivery_attempts &lt; 5</c>, ключ — <c>created_at</c> (порядок
/// очереди). <c>next_attempt_at</c> остаётся в предикате после индекса: он меняется при
/// каждой аренде, и держать его в ключе значило бы переписывать индекс на каждом тике.
/// NULL-путь (<c>next_attempt_at IS NULL</c>) фильтром не отсекается.
/// </para>
/// <para>
/// <c>user_id</c> — внешний ключ на <c>users</c> с <c>ON DELETE CASCADE</c>: очередь не
/// переживает удаление аккаунта. <c>booking_id</c> остаётся без FK: визит — снимок, и его
/// история не должна каскадно удалять напоминание до/после доставки.
/// </para>
/// </remarks>
public class PushNotificationConfiguration : IEntityTypeConfiguration<PushNotification>
{
    public void Configure(EntityTypeBuilder<PushNotification> builder)
    {
        builder.ToTable("push_notifications", table =>
        {
            table.HasCheckConstraint(
                "ck_push_notifications_kind",
                CheckConstraintSql.EnumIn<PushNotificationKind>("kind"));
            table.HasCheckConstraint(
                "ck_push_notifications_delivery",
                CheckConstraintSql.EnumIn<PushDelivery>("delivery"));
            table.HasCheckConstraint(
                "ck_push_notifications_delivery_attempts",
                FormattableString.Invariant(
                    $"delivery_attempts BETWEEN 0 AND {Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_push_notifications");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.StartAt).HasColumnName("start_at");

        builder.Property(x => x.SalonName)
            .HasColumnName("salon_name")
            .HasMaxLength(Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH);
        builder.Property(x => x.BarberName)
            .HasColumnName("barber_name")
            .HasMaxLength(Constants.MAX_NOTIFICATION_BARBER_NAME_LENGTH);
        builder.Property(x => x.ServiceName)
            .HasColumnName("service_name")
            .HasMaxLength(Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH);
        builder.Property(x => x.SalonTimeZoneId)
            .HasColumnName("salon_time_zone_id")
            .HasMaxLength(Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH);

        builder.Property(x => x.Delivery).HasColumnName("delivery").HasConversion<short>();
        builder.Property(x => x.DeliveryAttempts).HasColumnName("delivery_attempts");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH);

        // Удаление аккаунта уносит и его очередь напоминаний: без владельца строка —
        // псевдонимизированный след пользователя, который не должен жить до ретенции.
        // FK нужен, потому что push-таблица не переживает аккаунт (в отличие от истории визитов).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_push_notifications_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // На один визит — одно уведомление каждого вида: событие создания и напоминание
        // сосуществуют, повторный тик безопасен.
        builder.HasIndex(x => new { x.BookingId, x.Kind })
            .IsUnique()
            .HasDatabaseName("ux_push_notifications_booking_id");

        // Устройства получателя читаются по user_id при доставке (диагностика и будущий инбокс).
        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_push_notifications_user_id");

        // Вход доставки совпадает с запросом claim: delivery = Pending, попытки ещё не
        // исчерпаны, порядок по created_at. next_attempt_at (включая NULL) — фильтр после
        // индекса: он меняется при каждой аренде, и выносить его в ключ значило бы
        // переписывать индекс на каждом тике. Окончательно неудавшиеся строки в индекс
        // не входят — доставка их не читает.
        builder.HasIndex(x => x.CreatedAt)
            .HasFilter(FormattableString.Invariant(
                $"delivery = {(short)PushDelivery.Pending} AND delivery_attempts < {Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS}"))
            .HasDatabaseName("ix_push_notifications_dispatch");
    }
}
