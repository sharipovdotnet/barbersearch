using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>users</c> — агрегатного корня пользователя.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "ck_users_email",
                "position('@' in email) > 1");
            table.HasCheckConstraint(
                "ck_users_role",
                "role IN (1, 2, 3)");
        });

        builder.HasKey(x => x.Id).HasName("pk_users");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.Email).HasColumnName("email").IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(x => x.FirstName).HasColumnName("first_name").IsRequired();
        builder.Property(x => x.LastName).HasColumnName("last_name").IsRequired();
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number");
        builder.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
        builder.Property(x => x.IsEmailVerified).HasColumnName("is_email_verified");
        builder.Property(x => x.IsPhoneVerified).HasColumnName("is_phone_verified");
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.LastLoginAt).HasColumnName("last_login_at");

        // Это граница другого агрегата: навигации на BarberProfile нет, только идентификатор.
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.HasIndex(x => x.BarberProfileId)
            .HasDatabaseName("ix_users_barber_profile_id");
    }
}
