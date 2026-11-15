using BarberSearch.Domain;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>push_device_tokens</c> — адреса доставки push-уведомлений.
/// </summary>
/// <remarks>
/// <para>
/// Уникальный индекс <c>ux_push_device_tokens_token</c> — ключ устройства: токен не может
/// принадлежать двум аккаунтам одновременно. Повторная регистрация того же токена другим
/// пользователем — это передача владения (<c>ON CONFLICT (token) DO UPDATE</c>), а не ошибка.
/// </para>
/// <para>
/// Внешний ключ на <c>users</c> с <c>ON DELETE CASCADE</c>: удаление аккаунта уносит его
/// устройства, иначе push уходил бы на телефон, с которого аккаунт уже удалён.
/// </para>
/// <para>
/// Токен — строка FCM/APNs, а не произвольный текст: check-ограничения держат непустоту,
/// предел колонки и допустимые платформы, а <c>last_seen_at</c> нужен ограничению числа
/// устройств и суточной уборке.
/// </para>
/// </remarks>
public class PushDeviceTokenConfiguration : IEntityTypeConfiguration<PushDeviceToken>
{
    public void Configure(EntityTypeBuilder<PushDeviceToken> builder)
    {
        builder.ToTable("push_device_tokens", table =>
        {
            table.HasCheckConstraint(
                "ck_push_device_tokens_platform",
                CheckConstraintSql.EnumIn<PushPlatform>("platform"));
            table.HasCheckConstraint(
                "ck_push_device_tokens_token_length",
                FormattableString.Invariant(
                    $"char_length(token) BETWEEN 1 AND {Constants.MAX_PUSH_DEVICE_TOKEN_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_push_device_tokens");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Token)
            .HasColumnName("token")
            .HasMaxLength(Constants.MAX_PUSH_DEVICE_TOKEN_LENGTH)
            .IsRequired();
        builder.Property(x => x.Platform).HasColumnName("platform").HasConversion<short>();
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");

        // Удаление аккаунта уносит его устройства: без владельца токен бесполезен.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_push_device_tokens_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Один токен — одно устройство, а значит один владелец.
        builder.HasIndex(x => x.Token)
            .IsUnique()
            .HasDatabaseName("ux_push_device_tokens_token");

        // Устройства пользователя читаются пачкой при доставке; предел и уборка идут по user_id.
        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_push_device_tokens_user_id");
    }
}
