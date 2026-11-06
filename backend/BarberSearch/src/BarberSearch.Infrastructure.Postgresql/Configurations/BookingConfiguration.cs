using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>bookings</c> — агрегатного корня брони.
/// </summary>
/// <remarks>
/// <para>
/// UserId, BarberProfileId, BarberServiceId и SalonId — идентификаторы других агрегатов,
/// внешних ключей на них нет сознательно: навигаций через границу агрегата в домене тоже нет.
/// </para>
/// <para>
/// Бронь принадлежит либо зарегистрированному клиенту (<c>user_id</c>), либо гостю (walk-in):
/// тогда <c>user_id</c> = <c>NULL</c>, а имя и телефон гостя лежат в самой брони. Инвариант
/// «ровно один вид клиента» закреплён check-ограничением <c>ck_bookings_customer</c>.
/// </para>
/// </remarks>
public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", table =>
        {
            table.HasCheckConstraint(
                "ck_bookings_period",
                "end_at > start_at");
            table.HasCheckConstraint(
                "ck_bookings_price",
                "price >= 0");
            table.HasCheckConstraint(
                "ck_bookings_status",
                CheckConstraintSql.EnumIn<BookingStatus>("status"));
            table.HasCheckConstraint(
                "ck_bookings_idempotency_key",
                FormattableString.Invariant(
                    $"char_length(idempotency_key) BETWEEN 1 AND {Constants.MAX_IDEMPOTENCY_KEY_LENGTH}"));
            table.HasCheckConstraint(
                "ck_bookings_cancellation_reason",
                FormattableString.Invariant(
                    $"cancellation_reason IS NULL OR char_length(cancellation_reason) BETWEEN 1 AND {Constants.MAX_CANCELLATION_REASON_LENGTH}"));

            // Бронь — либо клиент, либо гость: оба вида одновременно недопустимы, и у гостя
            // обязательны имя и телефон. Это тот же инвариант, что проверяет Booking.Create,
            // но последняя линия защиты — база: прямой INSERT мимо домена его не обойдёт.
            table.HasCheckConstraint(
                "ck_bookings_customer",
                """
                (user_id IS NOT NULL AND guest_first_name IS NULL AND guest_last_name IS NULL AND guest_phone IS NULL) OR (user_id IS NULL AND guest_first_name IS NOT NULL AND guest_phone IS NOT NULL)
                """);

            // Поля гостя непустые и не длиннее varchar-колонки; NULL разрешён, потому что
            // у брони зарегистрированного клиента гостевых полей нет.
            table.HasCheckConstraint(
                "ck_bookings_guest_first_name",
                FormattableString.Invariant(
                    $"guest_first_name IS NULL OR char_length(btrim(guest_first_name)) BETWEEN 1 AND {Constants.MAX_GUEST_FIRST_NAME_LENGTH}"));
            table.HasCheckConstraint(
                "ck_bookings_guest_last_name",
                FormattableString.Invariant(
                    $"guest_last_name IS NULL OR char_length(btrim(guest_last_name)) BETWEEN 1 AND {Constants.MAX_GUEST_LAST_NAME_LENGTH}"));
            table.HasCheckConstraint(
                "ck_bookings_guest_phone",
                FormattableString.Invariant(
                    $"guest_phone IS NULL OR char_length(btrim(guest_phone)) BETWEEN 1 AND {Constants.MAX_GUEST_PHONE_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_bookings");

        // Оптимистическая блокировка: Cancel/Complete по устаревшему снимку не перезапишет
        // уже выполненный переход (R-05). Ошибка маппится в 409 booking.update.concurrencyConflict.
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // NULL — гость (walk-in): аккаунт для него не создаётся, контакт лежит ниже.
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.GuestFirstName).HasColumnName("guest_first_name")
            .HasMaxLength(Constants.MAX_GUEST_FIRST_NAME_LENGTH);
        builder.Property(x => x.GuestLastName).HasColumnName("guest_last_name")
            .HasMaxLength(Constants.MAX_GUEST_LAST_NAME_LENGTH);
        builder.Property(x => x.GuestPhone).HasColumnName("guest_phone")
            .HasMaxLength(Constants.MAX_GUEST_PHONE_LENGTH);
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");
        builder.Property(x => x.BarberServiceId).HasColumnName("barber_service_id");
        builder.Property(x => x.SalonId).HasColumnName("salon_id");
        builder.Property(x => x.StartAt).HasColumnName("start_at");
        builder.Property(x => x.EndAt).HasColumnName("end_at");
        builder.Property(x => x.Price).HasColumnName("price")
            .HasPrecision(ColumnPrecision.MONEY_PRECISION, ColumnPrecision.MONEY_SCALE);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
        builder.Property(x => x.CustomerNote).HasColumnName("customer_note");
        builder.Property(x => x.BarberNote).HasColumnName("barber_note");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason")
            .HasMaxLength(Constants.MAX_CANCELLATION_REASON_LENGTH);

        // Ключ идемпотентности защищает от повторной брони при обрыве сети у клиента.
        // Область уникальности — пара (пользователь, ключ): разные клиенты могут
        // сгенерировать одинаковую строку, и это не должно ломать запись.
        // NULL в user_id этим индексом не конфликтует (PostgreSQL считает NULL различными),
        // поэтому для гостя ниже добавлен отдельный частичный уникальный индекс.
        builder.HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_bookings_user_id_idempotency_key");

        // Гостевой ключ идемпотентности: у гостя user_id = NULL, поэтому скоуп —
        // (barber_profile_id, idempotency_key). Повтор запроса того же барбера с тем же
        // ключом обязан вернуть существующую бронь, а не создать вторую.
        builder.HasIndex(x => new { x.BarberProfileId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("user_id IS NULL")
            .HasDatabaseName("ux_bookings_barber_profile_id_idempotency_key_guest");

        // Расчёт свободных слотов: range scan по (барбер, день). Отменённые (3), завершённые (4)
        // и неявившиеся (5) слот не занимают, поэтому индекс частичный.
        // Само пересечение интервалов у одного барбера запрещает exclusion-ограничение
        // ex_bookings_no_overlap из первой миграции: EF Core такие ограничения не выражает,
        // поэтому оно живёт только в SQL миграции и здесь не описано.
        builder.HasIndex(x => new { x.BarberProfileId, x.StartAt })
            .HasFilter(CheckConstraintSql.EnumIn(
                "status",
                BookingStatus.Pending,
                BookingStatus.Confirmed))
            .HasDatabaseName("ix_bookings_barber_profile_id_start_at_active");

        // Слоты считаются по паре (барбер, салон), поэтому салон тоже в ключе.
        builder.HasIndex(x => new { x.SalonId, x.StartAt })
            .HasDatabaseName("ix_bookings_salon_id_start_at");

        // Список броней клиента; гостевые строки сюда не попадают (user_id = NULL).
        builder.HasIndex(x => new { x.UserId, x.StartAt })
            .HasDatabaseName("ix_bookings_user_id_start_at");
    }
}
