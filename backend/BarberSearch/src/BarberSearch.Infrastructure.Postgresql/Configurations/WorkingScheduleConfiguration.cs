using BarberSearch.Domain.WorkingSchedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>working_schedules</c> — недельного расписания барбера
/// в конкретном салоне (дочерняя сущность агрегата <c>BarberSalon</c>).
/// </summary>
public class WorkingScheduleConfiguration : IEntityTypeConfiguration<WorkingSchedule>
{
    public void Configure(EntityTypeBuilder<WorkingSchedule> builder)
    {
        builder.ToTable("working_schedules", table =>
        {
            table.HasCheckConstraint(
                "ck_working_schedules_day_of_week",
                "day_of_week BETWEEN 0 AND 6");

            // В нерабочий день время не задаётся, в рабочий — конец позже начала.
            table.HasCheckConstraint(
                "ck_working_schedules_time_range",
                "is_day_off OR end_time > start_time");
        });

        builder.HasKey(x => x.Id).HasName("pk_working_schedules");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberSalonId).HasColumnName("barber_salon_id");
        builder.Property(x => x.DayOfWeek).HasColumnName("day_of_week").HasConversion<int>();
        builder.Property(x => x.StartTime).HasColumnName("start_time");
        builder.Property(x => x.EndTime).HasColumnName("end_time");
        builder.Property(x => x.IsDayOff).HasColumnName("is_day_off");

        builder.HasOne(x => x.BarberSalon)
            .WithMany(salon => salon.WorkingSchedules)
            .HasForeignKey(x => x.BarberSalonId)
            .HasConstraintName("fk_working_schedules_barber_salons_barber_salon_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Расписание принадлежит паре (барбер, салон): на один день недели — одна запись.
        builder.HasIndex(x => new { x.BarberSalonId, x.DayOfWeek })
            .IsUnique()
            .HasDatabaseName("ux_working_schedules_barber_salon_id_day_of_week");
    }
}
