using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>bookings</c> — агрегатного корня брони.
/// </summary>
/// <remarks>
/// UserId, BarberProfileId, BarberServiceId и SalonId — идентификаторы других агрегатов,
/// внешних ключей на них нет сознательно: навигаций через границу агрегата в домене тоже нет.
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
                "status IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint(
                "ck_bookings_idempotency_key",
                FormattableString.Invariant(
                    $"char_length(idempotency_key) BETWEEN 1 AND {Constants.MAX_IDEMPOTENCY_KEY_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_bookings");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");
        builder.Property(x => x.BarberServiceId).HasColumnName("barber_service_id");
        builder.Property(x => x.SalonId).HasColumnName("salon_id");
        builder.Property(x => x.StartAt).HasColumnName("start_at");
        builder.Property(x => x.EndAt).HasColumnName("end_at");
        builder.Property(x => x.Price).HasColumnName("price").HasPrecision(10, 2);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
        builder.Property(x => x.CustomerNote).HasColumnName("customer_note");
        builder.Property(x => x.BarberNote).HasColumnName("barber_note");

        // Ключ идемпотентности защищает от повторной брони при обрыве сети у клиента.
        // Область уникальности — пара (пользователь, ключ): разные клиенты могут
        // сгенерировать одинаковую строку, и это не должно ломать запись.
        builder.HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_bookings_user_id_idempotency_key");

        // Расчёт свободных слотов: range scan по (барбер, день). Отменённые (3), завершённые (4)
        // и неявившиеся (5) слот не занимают, поэтому индекс частичный.
        builder.HasIndex(x => new { x.BarberProfileId, x.StartAt })
            .HasFilter("status IN (1, 2)")
            .HasDatabaseName("ix_bookings_barber_profile_id_start_at_active");

        // Слоты считаются по паре (барбер, салон), поэтому салон тоже в ключе.
        builder.HasIndex(x => new { x.SalonId, x.StartAt })
            .HasDatabaseName("ix_bookings_salon_id_start_at");

        // Список броней клиента.
        builder.HasIndex(x => new { x.UserId, x.StartAt })
            .HasDatabaseName("ix_bookings_user_id_start_at");
    }
}
