using BarberSearch.Domain;
using BarberSearch.Domain.EmailChanges;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>email_change_tokens</c>: запрошенный новый адрес, одноразовый хеш,
/// срок, потребление и отзыв.
/// </summary>
/// <remarks>
/// <para>
/// Открытый токен здесь не хранится: колонка <c>token_hash</c> — SHA-256 в шестнадцатеричном
/// виде. Частичный уникальный индекс <c>ux_email_change_tokens_active</c> — последняя защита
/// от гонки двух одновременных заявок: у пользователя не может быть двух неиспользованных
/// и неотозванных строк.
/// </para>
/// <para>
/// Индекс <c>ux_email_change_tokens_hash</c> — единственная защита от коллизии CSPRNG.
/// Она практически невозможна, поэтому нарушение этого индекса не превращается в бизнес-код.
/// </para>
/// <para>
/// <c>new_email</c> хранится как <c>varchar(254)</c>, а не <c>citext</c>: домен нормализует
/// адрес до записи, поэтому регистр в базе уже не нужен, а check-ограничения повторяют
/// формат <c>ck_users_email</c>.
/// </para>
/// </remarks>
public class EmailChangeTokenConfiguration
    : IEntityTypeConfiguration<EmailChangeToken>
{
    public void Configure(EntityTypeBuilder<EmailChangeToken> builder)
    {
        builder.ToTable("email_change_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_email_change_tokens_token_hash",
                FormattableString.Invariant(
                    $"char_length(token_hash) = {EmailChangeToken.HASH_LENGTH}"));
            table.HasCheckConstraint(
                "ck_email_change_tokens_new_email",
                "position('@' in new_email) > 1");
            table.HasCheckConstraint(
                "ck_email_change_tokens_new_email_length",
                FormattableString.Invariant(
                    $"char_length(new_email) BETWEEN {Constants.MIN_EMAIL_LENGTH} AND {Constants.MAX_EMAIL_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_email_change_tokens");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");

        // Предел btree-индекса и контракт домена: длина адреса ограничена и в DDL.
        builder.Property(x => x.NewEmail)
            .HasColumnName("new_email")
            .HasMaxLength(Constants.MAX_EMAIL_LENGTH)
            .IsRequired();

        builder.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();

        // IssuedAt — бизнес-время из TimeProvider: по нему считается кулдаун, а не по CreatedAt.
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        // Удаление пользователя уносит все его заявки на смену адреса.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_email_change_tokens_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_email_change_tokens_hash");

        // Не более одной неиспользованной и неотозванной строки на пользователя. Фильтр
        // повторяет предикат активной строки в репозитории; истёкшая, но не отозванная
        // строка тоже считается активной, пока её не погасит новая заявка или уборка.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("consumed_at IS NULL AND revoked_at IS NULL")
            .HasDatabaseName("ux_email_change_tokens_active");

        // Диапазонный предикат фоновой уборки: DELETE … WHERE expires_at < cutoff.
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_email_change_tokens_expires_at");
    }
}
