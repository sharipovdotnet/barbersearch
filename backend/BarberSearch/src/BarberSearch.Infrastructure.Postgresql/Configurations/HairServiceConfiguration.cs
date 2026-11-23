using BarberSearch.Domain;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Specializations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>hair_services</c> — справочника услуг платформы.
/// </summary>
/// <remarks>
/// Услуга принадлежит ровно одному направлению (<c>specializations</c>): на витрине клиент
/// сначала выбирает направление, затем услугу. FK без навигации — это граница другого
/// агрегата, поэтому наружу торчит только идентификатор, а удаление направления запрещено,
/// пока на нём висят услуги.
/// </remarks>
public class HairServiceConfiguration : IEntityTypeConfiguration<HairService>
{
    public void Configure(EntityTypeBuilder<HairService> builder)
    {
        builder.ToTable("hair_services", table =>
        {
            table.HasCheckConstraint(
                "ck_hair_services_name",
                "char_length(btrim(name)) > 0");
            table.HasCheckConstraint(
                "ck_hair_services_category",
                CheckConstraintSql.EnumIn<ServiceCategory>("category"));
            table.HasCheckConstraint(
                "ck_hair_services_default_duration_minutes",
                FormattableString.Invariant(
                    $"default_duration_minutes BETWEEN {Constants.MIN_DURATION_MINUTES} AND {Constants.MAX_DURATION_MINUTES}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_hair_services");

        // Оптимистическая блокировка: конкурентный UPDATE не перезаписывает чужую правку (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<int>();

        // Это граница другого агрегата: навигации на Specialization нет, только идентификатор.
        builder.Property(x => x.SpecializationId).HasColumnName("specialization_id");

        builder.HasOne<Specialization>()
            .WithMany()
            .HasForeignKey(x => x.SpecializationId)
            .HasConstraintName("fk_hair_services_specializations_specialization_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.DefaultDurationMinutes).HasColumnName("default_duration_minutes");
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        builder.HasIndex(x => new { x.Category, x.IsActive })
            .HasDatabaseName("ix_hair_services_category_is_active");

        // Двухшаговый выбор на витрине: услуги выбранного направления.
        builder.HasIndex(x => new { x.SpecializationId, x.IsActive })
            .HasDatabaseName("ix_hair_services_specialization_id_is_active");
    }
}
