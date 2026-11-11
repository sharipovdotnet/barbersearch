using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
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
                CheckConstraintSql.EnumIn<UserRole>("role"));
            table.HasCheckConstraint(
                "ck_users_security_stamp",
                "char_length(security_stamp) > 0");
            // Счётчик неудачных входов (R-21) не может быть отрицательным; верхнюю границу
            // держит домен, а не БД: там же считается задержка блокировки.
            table.HasCheckConstraint(
                "ck_users_failed_login_attempts",
                "failed_login_attempts >= 0");
            // Согласие (#42): редакция и время появляются вместе либо остаются NULL у обоих.
            // Старые и админ-созданные аккаунты — второй случай, и backfill для них
            // невозможен: согласие, которого не было, доказать нельзя.
            table.HasCheckConstraint(
                "ck_users_consent",
                "(consent_policy_version IS NULL) = (consented_at IS NULL) " +
                "AND (consent_policy_version IS NULL OR char_length(consent_policy_version) > 0)");

            // Премодерация аватара: статус — одно из трёх значений, причина отказа не пустая
            // и не длиннее 500 символов. Те же правила проверяет домен, ограничение —
            // последняя линия защиты.
            table.HasCheckConstraint(
                "ck_users_avatar_moderation_status",
                CheckConstraintSql.EnumIn<PhotoModerationStatus>("avatar_moderation_status"));
            table.HasCheckConstraint(
                "ck_users_avatar_rejection_reason",
                FormattableString.Invariant(
                    $"avatar_rejection_reason IS NULL OR char_length(avatar_rejection_reason) BETWEEN 1 AND {Constants.MAX_PHOTO_REJECTION_REASON_LENGTH}"));
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

        // Административная блокировка аватара: NOT NULL с DEFAULT false — миграция
        // AddUserAvatarBlocked. Существующие строки получают false, то есть фото видимо.
        builder.Property(x => x.IsAvatarBlocked)
            .HasColumnName("is_avatar_blocked")
            .IsRequired()
            .HasDefaultValue(false);

        // Премодерация аватара: enum хранится числом в smallint (значения стабильны).
        // Default 2 (Approved) описывает все существующие строки и аккаунты без аватара:
        // миграция AddPhotoModeration не перепроверяет старые кадры.
        builder.Property(x => x.AvatarModerationStatus)
            .HasColumnName("avatar_moderation_status")
            .HasConversion<short>()
            .HasDefaultValue(PhotoModerationStatus.Approved);
        builder.Property(x => x.AvatarSubmittedAt).HasColumnName("avatar_submitted_at");
        builder.Property(x => x.AvatarReviewedAt).HasColumnName("avatar_reviewed_at");
        builder.Property(x => x.AvatarReviewedByUserId).HasColumnName("avatar_reviewed_by_user_id");
        builder.Property(x => x.AvatarRejectionReason)
            .HasColumnName("avatar_rejection_reason")
            .HasMaxLength(Constants.MAX_PHOTO_REJECTION_REASON_LENGTH);

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

        // Подтверждаемое согласие (#42): редакция Политики, серверное время и IP.
        // IP — персональные данные: колонка существует только здесь и никуда дальше не
        // проецируется (ни в API, ни в audit_log, ни в логи).
        builder.Property(x => x.ConsentPolicyVersion)
            .HasColumnName("consent_policy_version")
            .HasColumnType("text")
            .HasMaxLength(UserConsent.MAX_POLICY_VERSION_LENGTH);
        builder.Property(x => x.ConsentedAt).HasColumnName("consented_at");
        builder.Property(x => x.ConsentIp)
            .HasColumnName("consent_ip")
            .HasColumnType("inet");

        // FK без навигации: привязка пользователя к профилю не может ссылаться на
        // несуществующий профиль (LinkUserBarberProfileHandler полагается на БД).
        builder.HasOne<BarberProfile>()
            .WithMany()
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_users_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Кто проверил аватар: удаление аккаунта администратора обнуляет автора решения,
        // а не удаляет пользователя.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AvatarReviewedByUserId)
            .HasConstraintName("fk_users_users_avatar_reviewed_by_user_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.HasIndex(x => x.BarberProfileId)
            .HasDatabaseName("ix_users_barber_profile_id");

        // Очередь модерации аватаров: только Pending, FIFO по avatar_submitted_at, id —
        // хвост курсора. Частичный индекс: аккаунты без аватара в очередь не попадают.
        builder.HasIndex(x => new { x.AvatarModerationStatus, x.AvatarSubmittedAt, x.Id })
            .HasDatabaseName("ix_users_avatar_moderation_status_submitted_at")
            .HasFilter("avatar_url IS NOT NULL");

        // Индекс под FK: удаление аккаунта администратора проверяет ссылки.
        builder.HasIndex(x => x.AvatarReviewedByUserId)
            .HasDatabaseName("ix_users_avatar_reviewed_by_user_id");
    }
}
