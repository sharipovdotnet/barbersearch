using BarberSearch.Domain.BarberSpecializations;
using BarberSearch.Domain.Specializations;
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

        // xmin и на дочерней строке: UPDATE ребёнка не трогает корень агрегата (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Это граница другого агрегата: навигации на Specialization нет, только идентификатор.
        builder.Property(x => x.SpecializationId).HasColumnName("specialization_id");

        // FK без навигации: новая строка barber_specializations не может ссылаться на
        // несуществующую специализацию. Справочник деактивируется, а не удаляется.
        builder.HasOne<Specialization>()
            .WithMany()
            .HasForeignKey(x => x.SpecializationId)
            .HasConstraintName("fk_barber_specializations_specializations_specialization_id")
            .OnDelete(DeleteBehavior.Restrict);

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
