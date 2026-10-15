using BarberSearch.Domain.FavoriteBarbers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>favorite_barbers</c> — избранных барберов пользователя
/// (дочерняя сущность агрегата <c>User</c>).
/// </summary>
public class FavoriteBarberConfiguration : IEntityTypeConfiguration<FavoriteBarber>
{
    public void Configure(EntityTypeBuilder<FavoriteBarber> builder)
    {
        builder.ToTable("favorite_barbers");

        builder.HasKey(x => x.Id).HasName("pk_favorite_barbers");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");

        // Это граница другого агрегата: навигации на BarberProfile нет, только идентификатор.
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        builder.HasOne(x => x.User)
            .WithMany(user => user.FavoriteBarbers)
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_favorite_barbers_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Повторное добавление в избранное запрещено и в домене.
        builder.HasIndex(x => new { x.UserId, x.BarberProfileId })
            .IsUnique()
            .HasDatabaseName("ux_favorite_barbers_user_id_barber_profile_id");

        builder.HasIndex(x => x.BarberProfileId)
            .HasDatabaseName("ix_favorite_barbers_barber_profile_id");
    }
}
