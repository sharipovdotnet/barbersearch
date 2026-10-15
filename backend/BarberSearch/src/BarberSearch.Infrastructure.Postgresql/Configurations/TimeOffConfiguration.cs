using BarberSearch.Domain.TimeOffs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>time_offs</c> — отпусков барбера (дочерняя сущность агрегата профиля).
/// </summary>
public class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> builder)
    {
        builder.ToTable("time_offs", table =>
            table.HasCheckConstraint(
                "ck_time_offs_period",
                "end_at > start_at"));

        builder.HasKey(x => x.Id).HasName("pk_time_offs");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");
        builder.Property(x => x.StartAt).HasColumnName("start_at");
        builder.Property(x => x.EndAt).HasColumnName("end_at");
        builder.Property(x => x.Reason).HasColumnName("reason");

        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.TimeOffs)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_time_offs_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Поиск отпусков барбера при расчёте слотов.
        builder.HasIndex(x => new { x.BarberProfileId, x.StartAt })
            .HasDatabaseName("ix_time_offs_barber_profile_id_start_at");
    }
}
