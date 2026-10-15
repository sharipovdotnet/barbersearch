using BarberSearch.Domain.BarberSalons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_salons</c> — салонов, в которых работает барбер
/// (дочерняя сущность агрегата профиля).
/// </summary>
public class BarberSalonConfiguration : IEntityTypeConfiguration<BarberSalon>
{
    public void Configure(EntityTypeBuilder<BarberSalon> builder)
    {
        builder.ToTable("barber_salons");

        builder.HasKey(x => x.Id).HasName("pk_barber_salons");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Это граница другого агрегата: навигации на Salon нет, только идентификатор.
        builder.Property(x => x.SalonId).HasColumnName("salon_id");

        builder.Property(x => x.IsPrimary).HasColumnName("is_primary");

        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.Salons)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_barber_salons_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Дубликат «барбер уже работает в этом салоне» запрещён и в домене.
        builder.HasIndex(x => new { x.BarberProfileId, x.SalonId })
            .IsUnique()
            .HasDatabaseName("ux_barber_salons_barber_profile_id_salon_id");

        // Основной салон у барбера ровно один — частичный уникальный индекс.
        builder.HasIndex(x => x.BarberProfileId)
            .IsUnique()
            .HasFilter("is_primary")
            .HasDatabaseName("ux_barber_salons_primary_per_profile");

        // Барберы салона: выборка в обратную сторону.
        builder.HasIndex(x => x.SalonId)
            .HasDatabaseName("ix_barber_salons_salon_id");
    }
}
