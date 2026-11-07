using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_notifications</c> — инбокс напоминаний барберу.
/// </summary>
/// <remarks>
/// <para>
/// Таблица — снимок на момент создания: названия услуги и салона и пояс салона не
/// пересчитываются, поэтому здесь нет внешних ключей на <c>bookings</c>, <c>salons</c> и
/// <c>hair_services</c>. Уведомление переживает удаление услуги и переименование салона.
/// </para>
/// <para>
/// Дедупликация — на уровне БД, а не обработчика: частичные уникальные индексы
/// <c>ux_barber_notifications_upcoming</c> (<c>booking_id where kind = 1</c>) и
/// <c>ux_barber_notifications_digest</c>
/// (<c>user_id, digest_local_date where kind = 2</c>) делают повторный тик и второй
/// экземпляр приложения безопасными. Вставка идёт <c>ON CONFLICT DO NOTHING</c>.
/// </para>
/// <para>
/// <c>ix_barber_notifications_delivery</c> фильтрует <c>delivery in (0, 3)</c> — ожидающие
/// и окончательно неудавшиеся строки. Ожидающие — вход доставки; окончательно неудавшиеся
/// оставлены в индексе, потому что их читает диагностика и будущая поверхность повторной
/// отправки, а частичный индекс дешевле полного.
/// </para>
/// </remarks>
public class BarberNotificationConfiguration : IEntityTypeConfiguration<BarberNotification>
{
    public void Configure(EntityTypeBuilder<BarberNotification> builder)
    {
        builder.ToTable("barber_notifications", table =>
        {
            table.HasCheckConstraint(
                "ck_barber_notifications_kind",
                CheckConstraintSql.EnumIn<BarberNotificationKind>("kind"));
            table.HasCheckConstraint(
                "ck_barber_notifications_delivery",
                CheckConstraintSql.EnumIn<BarberNotificationDelivery>("delivery"));
            table.HasCheckConstraint(
                "ck_barber_notifications_delivery_attempts",
                FormattableString.Invariant(
                    $"delivery_attempts BETWEEN 0 AND {Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS}"));

            // Вид уведомления определяет обязательные поля: напоминание о визите живёт
            // вокруг брони и её начала, дайджест — вокруг локальных суток и числа записей.
            // Домен проверяет то же самое; база — последняя линия защиты от прямого INSERT.
            table.HasCheckConstraint(
                "ck_barber_notifications_shape",
                """
                (kind = 1 AND booking_id IS NOT NULL AND start_at IS NOT NULL)
                OR (kind = 2 AND bookings_count IS NOT NULL AND digest_local_date IS NOT NULL)
                """);
        });

        builder.HasKey(x => x.Id).HasName("pk_barber_notifications");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.StartAt).HasColumnName("start_at");

        builder.Property(x => x.ServiceName)
            .HasColumnName("service_name")
            .HasMaxLength(Constants.MAX_NOTIFICATION_SERVICE_NAME_LENGTH);
        builder.Property(x => x.SalonName)
            .HasColumnName("salon_name")
            .HasMaxLength(Constants.MAX_NOTIFICATION_SALON_NAME_LENGTH);
        builder.Property(x => x.SalonTimeZoneId)
            .HasColumnName("salon_time_zone_id")
            .HasMaxLength(Constants.MAX_NOTIFICATION_TIME_ZONE_ID_LENGTH);

        builder.Property(x => x.BookingsCount).HasColumnName("bookings_count");
        builder.Property(x => x.DigestLocalDate).HasColumnName("digest_local_date");

        builder.Property(x => x.ReadAt).HasColumnName("read_at");
        builder.Property(x => x.Delivery).HasColumnName("delivery").HasConversion<short>();
        builder.Property(x => x.DeliveryAttempts).HasColumnName("delivery_attempts");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(Constants.MAX_NOTIFICATION_LAST_ERROR_LENGTH);

        // Инбокс: страница всегда читается свежими сверху, ведущая колонка — получатель.
        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_barber_notifications_user_created");

        // Один визит — одно напоминание. Условие совпадает с CHECK kind = 1 (Upcoming).
        builder.HasIndex(x => x.BookingId)
            .IsUnique()
            .HasFilter(FormattableString.Invariant(
                $"kind = {(short)BarberNotificationKind.Upcoming}"))
            .HasDatabaseName("ux_barber_notifications_upcoming");

        // Один дайджест в день на барбера, а не на салон (решение D5).
        builder.HasIndex(x => new { x.UserId, x.DigestLocalDate })
            .IsUnique()
            .HasFilter(FormattableString.Invariant(
                $"kind = {(short)BarberNotificationKind.Digest}"))
            .HasDatabaseName("ux_barber_notifications_digest");

        // Вход доставки: ожидающие строки в порядке создания; окончательно неудавшиеся
        // оставлены в индексе для диагностики.
        builder.HasIndex(x => new { x.NextAttemptAt, x.CreatedAt })
            .HasFilter(FormattableString.Invariant(
                $"delivery IN ({(short)BarberNotificationDelivery.Pending}, {(short)BarberNotificationDelivery.Failed})"))
            .HasDatabaseName("ix_barber_notifications_delivery");
    }
}
