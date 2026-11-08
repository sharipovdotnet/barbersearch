using BarberSearch.Domain.EmailVerifications;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>email_verification_tokens</c>: одноразовый хеш, срок, потребление
/// и отзыв.
/// </summary>
/// <remarks>
/// <para>
/// Открытый токен здесь не хранится: колонка <c>token_hash</c> — SHA-256 в шестнадцатеричном
/// виде. Частичный уникальный индекс <c>ux_email_verification_tokens_active</c> — последняя
/// защита от гонки двух одновременных переотправок: у пользователя не может быть двух
/// неиспользованных и неотозванных строк.
/// </para>
/// <para>
/// Индекс <c>ux_email_verification_tokens_hash</c> — единственная защита от коллизии CSPRNG.
/// Она практически невозможна, поэтому нарушение этого индекса не превращается в бизнес-код.
/// </para>
/// </remarks>
public class EmailVerificationTokenConfiguration
    : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_email_verification_tokens_token_hash",
                FormattableString.Invariant(
                    $"char_length(token_hash) = {EmailVerificationToken.HASH_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_email_verification_tokens");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();

        // IssuedAt — бизнес-время из TimeProvider: по нему считается кулдаун, а не по CreatedAt.
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        // Удаление пользователя уносит все его токены подтверждения.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_email_verification_tokens_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_email_verification_tokens_hash");

        // Не более одной неиспользованной и неотозванной строки на пользователя. Фильтр
        // повторяет предикат активной строки в репозитории; истёкшая, но не отозванная
        // строка тоже считается активной, пока её не погасит переотправка или уборка.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("consumed_at IS NULL AND revoked_at IS NULL")
            .HasDatabaseName("ux_email_verification_tokens_active");

        // Диапазонный предикат фоновой уборки: DELETE … WHERE expires_at < cutoff.
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_email_verification_tokens_expires_at");
    }
}
