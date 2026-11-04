using BarberSearch.Domain.WorkingSchedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>schedule_overrides</c> — исключений на конкретные даты
/// (дочерняя сущность агрегата <c>BarberSalon</c>).
/// </summary>
public class ScheduleOverrideConfiguration : IEntityTypeConfiguration<ScheduleOverride>
{
    public void Configure(EntityTypeBuilder<ScheduleOverride> builder)
    {
        builder.ToTable("schedule_overrides", table =>
        {
            // В нерабочий день время не задаётся, в рабочий — конец позже начала.
            table.HasCheckConstraint(
                "ck_schedule_overrides_time_range",
                "is_day_off OR end_time > start_time");
        });

        builder.HasKey(x => x.Id).HasName("pk_schedule_overrides");

        // xmin и на дочерней строке: UPDATE ребёнка не трогает корень агрегата (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberSalonId).HasColumnName("barber_salon_id");
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.IsDayOff).HasColumnName("is_day_off");
        builder.Property(x => x.StartTime).HasColumnName("start_time");
        builder.Property(x => x.EndTime).HasColumnName("end_time");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.HasOne(x => x.BarberSalon)
            .WithMany(salon => salon.ScheduleExceptions)
            .HasForeignKey(x => x.BarberSalonId)
            .HasConstraintName("fk_schedule_overrides_barber_salons_barber_salon_id")
            .OnDelete(DeleteBehavior.Cascade);

        // На одну дату в паре (барбер, салон) — одно исключение.
        builder.HasIndex(x => new { x.BarberSalonId, x.Date })
            .IsUnique()
            .HasDatabaseName("ux_schedule_overrides_barber_salon_id_date");
    }
}
