using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Salons;
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

        // xmin и на дочерней строке: UPDATE ребёнка не трогает корень агрегата (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Это граница другого агрегата: навигации на Salon нет, только идентификатор.
        builder.Property(x => x.SalonId).HasColumnName("salon_id");

        // FK без навигации: ссылочную целостность держит БД, а домен по-прежнему
        // видит только Guid. Салон не удаляется, а деактивируется, поэтому Restrict
        // не мешает рабочему сценарию; R-02.
        builder.HasOne<Salon>()
            .WithMany()
            .HasForeignKey(x => x.SalonId)
            .HasConstraintName("fk_barber_salons_salons_salon_id")
            .OnDelete(DeleteBehavior.Restrict);

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

        // Основной салон у барбера ровно один. В БД это не частичный уникальный индекс,
        // а частичное DEFERRABLE-ограничение (миграция DeferrablePrimarySalonConstraint):
        // SetPrimarySalon за один SaveChanges снимает старый флаг и ставит новый, и
        // не-DEFERRABLE индекс отклонял бы промежуточное состояние «два основных» (R-06).
        // EF Core не умеет моделировать частичные exclusion-ограничения, поэтому
        // инвариант целиком принадлежит БД и в модели намеренно не объявлен.

        // Барберы салона: выборка в обратную сторону.
        builder.HasIndex(x => x.SalonId)
            .HasDatabaseName("ix_barber_salons_salon_id");
    }
}
