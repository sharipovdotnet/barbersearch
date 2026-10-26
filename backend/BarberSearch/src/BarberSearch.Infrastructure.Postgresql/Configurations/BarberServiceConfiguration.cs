using BarberSearch.Domain;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.HairServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_services</c> — цены и длительности услуги
/// у конкретного барбера (дочерняя сущность агрегата профиля).
/// </summary>
public class BarberServiceConfiguration : IEntityTypeConfiguration<BarberService>
{
    public void Configure(EntityTypeBuilder<BarberService> builder)
    {
        builder.ToTable("barber_services", table =>
        {
            table.HasCheckConstraint(
                "ck_barber_services_price",
                "price >= 0");
            table.HasCheckConstraint(
                "ck_barber_services_duration_minutes",
                FormattableString.Invariant(
                    $"duration_minutes BETWEEN {Constants.MIN_DURATION_MINUTES} AND {Constants.MAX_DURATION_MINUTES}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_barber_services");

        // xmin и на дочерней строке: UPDATE ребёнка не трогает корень агрегата (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Это граница другого агрегата: навигации на HairService нет, только идентификатор.
        builder.Property(x => x.HairServiceId).HasColumnName("hair_service_id");

        // FK без навигации: новая строка barber_services не может ссылаться на
        // несуществующую услугу справочника. Справочник деактивируется, а не удаляется.
        builder.HasOne<HairService>()
            .WithMany()
            .HasForeignKey(x => x.HairServiceId)
            .HasConstraintName("fk_barber_services_hair_services_hair_service_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Price).HasColumnName("price")
            .HasPrecision(ColumnPrecision.MONEY_PRECISION, ColumnPrecision.MONEY_SCALE);
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.Services)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_barber_services_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        // У барбера услуга каталога встречается один раз.
        builder.HasIndex(x => new { x.BarberProfileId, x.HairServiceId })
            .IsUnique()
            .HasDatabaseName("ux_barber_services_barber_profile_id_hair_service_id");

        // Тот же join в обратную сторону: «кто оказывает эту услугу».
        builder.HasIndex(x => new { x.HairServiceId, x.BarberProfileId })
            .HasDatabaseName("ix_barber_services_hair_service_id_barber_profile_id");
    }
}
