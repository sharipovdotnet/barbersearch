using BarberSearch.Domain.BarberSpecializations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_specializations</c> — специализаций барбера
/// (дочерняя сущность агрегата профиля).
/// </summary>
public class BarberSpecializationConfiguration
    : IEntityTypeConfiguration<BarberSpecialization>
{
    public void Configure(EntityTypeBuilder<BarberSpecialization> builder)
    {
        builder.ToTable("barber_specializations");

        builder.HasKey(x => x.Id).HasName("pk_barber_specializations");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Это граница другого агрегата: навигации на Specialization нет, только идентификатор.
        builder.Property(x => x.SpecializationId).HasColumnName("specialization_id");

        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.BarberSpecializations)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_barber_specializations_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        // У барбера специализация встречается один раз.
        builder.HasIndex(x => new { x.BarberProfileId, x.SpecializationId })
            .IsUnique()
            .HasDatabaseName("ux_barber_specializations_barber_profile_id_specialization_id");

        // Поиск начинается с фильтра, а не с барбера, поэтому колонка фильтра идёт первой.
        builder.HasIndex(x => new { x.SpecializationId, x.BarberProfileId })
            .HasDatabaseName("ix_barber_specializations_specialization_id_barber_profile_id");
    }
}
