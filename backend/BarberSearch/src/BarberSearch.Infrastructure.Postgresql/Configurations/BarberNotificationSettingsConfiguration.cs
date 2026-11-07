using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_notification_settings</c> — настройки напоминаний барбера.
/// </summary>
/// <remarks>
/// <para>
/// Ключ — <c>user_id</c>: строка одна на барбера, потому что дайджест считается на человека
/// (решение D5). Внешний ключ на <c>users</c> с <c>ON DELETE CASCADE</c>: удаление аккаунта
/// уносит и настройки, иначе строка осталась бы без владельца.
/// </para>
/// <para>
/// Два уникальных индекса закрывают правила Telegram: один чат — один кабинет
/// (<c>telegram_chat_id</c>) и одноразовый код привязки, который не может быть выдан
/// двум барберам одновременно (<c>telegram_link_code_hash</c>). Оба индекса частичные:
/// <c>NULL</c> — нормальное состояние (чат не привязан, код не выдан).
/// </para>
/// </remarks>
public class BarberNotificationSettingsConfiguration
    : IEntityTypeConfiguration<BarberNotificationSettings>
{
    /// <summary>
    /// Длина SHA-256 в шестнадцатеричной записи — тот же формат, что у хеша кода привязки.
    /// </summary>
    private const int LINK_CODE_HASH_LENGTH = 64;

    public void Configure(EntityTypeBuilder<BarberNotificationSettings> builder)
    {
        builder.ToTable("barber_notification_settings", table =>
        {
            table.HasCheckConstraint(
                "ck_barber_notification_settings_lead_minutes",
                FormattableString.Invariant(
                    $"lead_minutes IN ({Constants.MIN_NOTIFICATION_LEAD_MINUTES}, {Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES}, {Constants.MAX_NOTIFICATION_LEAD_MINUTES})"));
            table.HasCheckConstraint(
                "ck_barber_notification_settings_digest_time",
                FormattableString.Invariant(
                    $"digest_time >= TIME '{Constants.MIN_NOTIFICATION_DIGEST_HOUR:00}:00:00' AND digest_time <= TIME '{Constants.MAX_NOTIFICATION_DIGEST_HOUR:00}:00:00' AND date_part('minute', digest_time)::int % {Constants.NOTIFICATION_DIGEST_TIME_STEP_MINUTES} = 0 AND date_part('second', digest_time) = 0"));
            table.HasCheckConstraint(
                "ck_barber_notification_settings_telegram_code",
                FormattableString.Invariant(
                    $"(telegram_link_code_hash IS NULL) = (telegram_link_code_expires_at IS NULL) AND (telegram_link_code_hash IS NULL OR char_length(telegram_link_code_hash) = {LINK_CODE_HASH_LENGTH})"));
        });

        builder.HasKey(x => x.UserId).HasName("pk_barber_notification_settings");

        builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.DigestEnabled).HasColumnName("digest_enabled");
        builder.Property(x => x.DigestTime).HasColumnName("digest_time");
        builder.Property(x => x.LeadMinutes).HasColumnName("lead_minutes");
        builder.Property(x => x.TelegramChatId).HasColumnName("telegram_chat_id");
        builder.Property(x => x.TelegramLinkedAt).HasColumnName("telegram_linked_at");
        builder.Property(x => x.TelegramLinkCodeHash)
            .HasColumnName("telegram_link_code_hash")
            .HasMaxLength(LINK_CODE_HASH_LENGTH);
        builder.Property(x => x.TelegramLinkCodeExpiresAt)
            .HasColumnName("telegram_link_code_expires_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Удаление пользователя уносит настройки: без владельца строка бесполезна.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_barber_notification_settings_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Один чат — один кабинет: второй барбер не привяжет тот же чат.
        builder.HasIndex(x => x.TelegramChatId)
            .IsUnique()
            .HasFilter("telegram_chat_id IS NOT NULL")
            .HasDatabaseName("ux_barber_notification_settings_telegram_chat_id");

        // Код привязки ищется вебхуком по хешу; уникальность исключает два одинаковых кода.
        builder.HasIndex(x => x.TelegramLinkCodeHash)
            .IsUnique()
            .HasFilter("telegram_link_code_hash IS NOT NULL")
            .HasDatabaseName("ux_barber_notification_settings_telegram_link_code_hash");
    }
}
