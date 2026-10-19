using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>refresh_tokens</c>: хеш токена, срок, отзыв и цепочка ротации.
/// </summary>
/// <remarks>
/// Открытый токен здесь не хранится: колонка <c>token_hash</c> — SHA-256 в шестнадцатеричном
/// виде, поэтому утечка таблицы не даёт готовых учётных данных.
/// </remarks>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_refresh_tokens_token_hash",
                "char_length(token_hash) = 64");
        });

        builder.HasKey(x => x.Id).HasName("pk_refresh_tokens");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        // Ссылка на строку, которой токен заменён при ротации. Навигации нет:
        // цепочка читается отдельным запросом по replaced_by.
        builder.Property(x => x.ReplacedBy).HasColumnName("replaced_by");

        // Удаление пользователя уносит все его refresh-токены.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_refresh_tokens_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Удаление строки не должно тянуть за собой заменённую: связь обнуляется.
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(x => x.ReplacedBy)
            .HasConstraintName("fk_refresh_tokens_refresh_tokens_replaced_by")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        builder.HasIndex(x => x.ReplacedBy)
            .HasDatabaseName("ix_refresh_tokens_replaced_by");
    }
}
