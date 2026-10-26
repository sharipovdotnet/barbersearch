using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
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
            // Длина ограничена на уровне БД, а не только доменом: иначе адрес длиннее
            // предела btree-индекса ронял вставку в 500 (R-03). citext, в свою очередь,
            // делает уникальность и сравнение email регистронезависимыми.
            table.HasCheckConstraint(
                "ck_users_email",
                $"char_length(email) BETWEEN {Constants.MIN_EMAIL_LENGTH} AND {Constants.MAX_EMAIL_LENGTH}");
            table.HasCheckConstraint(
                "ck_users_role",
                "role IN (1, 2, 3)");
            table.HasCheckConstraint(
                "ck_users_security_stamp",
                "char_length(security_stamp) > 0");
            // Счётчик неудачных входов (R-21) не может быть отрицательным; верхнюю границу
            // держит домен, а не БД: там же считается задержка блокировки.
            table.HasCheckConstraint(
                "ck_users_failed_login_attempts",
                "failed_login_attempts >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_users");

        // Оптимистическая блокировка: конкурентный UPDATE не перезаписывает чужую правку (R-05).
        builder.UseXminConcurrencyToken();

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // citext хранит канонический адрес (домен приводит регистр), а сравнение и
        // уникальный индекс делает регистронезависимыми сама БД. HasMaxLength фиксирует
        // контракт в модели, но DDL для citext длину не задаёт (у типа нет модификатора),
        // поэтому фактически длину ограничивает check-ограничение.
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasColumnType("citext")
            .HasMaxLength(Constants.MAX_EMAIL_LENGTH)
            .IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(x => x.FirstName).HasColumnName("first_name").IsRequired();
        builder.Property(x => x.LastName).HasColumnName("last_name").IsRequired();
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number");
        builder.Property(x => x.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(Constants.MAX_URL_LENGTH);
        builder.Property(x => x.IsEmailVerified).HasColumnName("is_email_verified");
        builder.Property(x => x.IsPhoneVerified).HasColumnName("is_phone_verified");
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.LastLoginAt).HasColumnName("last_login_at");

        // Защита входа от перебора (R-21): счётчик неудач, момент последней неудачи и конец
        // блокировки. Значения считает домен, база только хранит их.
        builder.Property(x => x.FailedLoginAttempts)
            .HasColumnName("failed_login_attempts")
            .IsRequired();
        builder.Property(x => x.LastFailedLoginAt).HasColumnName("last_failed_login_at");
        builder.Property(x => x.LockoutEnd).HasColumnName("lockout_end");

        // Отпечаток учётных данных: сверяется с claim'ом security_stamp на каждом запросе.
        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp").IsRequired();

        // Это граница другого агрегата: навигации на BarberProfile нет, только идентификатор.
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // FK без навигации: привязка пользователя к профилю не может ссылаться на
        // несуществующий профиль (LinkUserBarberProfileHandler полагается на БД).
        builder.HasOne<BarberProfile>()
            .WithMany()
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_users_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.HasIndex(x => x.BarberProfileId)
            .HasDatabaseName("ix_users_barber_profile_id");
    }
}
