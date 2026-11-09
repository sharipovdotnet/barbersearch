using BarberSearch.Domain.EmailOutbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>email_outbox</c> — очередь исходящих писем.
/// </summary>
/// <remarks>
/// <para>
/// Уникальный индекс <c>ux_email_outbox_deduplication_key</c> — последняя защита от двух строк
/// на одно письмо: обработчик пишет id выданного токена, поэтому повторная вставка того же
/// письма невозможна даже при гонке двух запросов.
/// </para>
/// <para>
/// Индекс <c>ix_email_outbox_dispatch</c> повторяет предикат диспетчера:
/// <c>sent_at IS NULL AND failed_at IS NULL AND next_attempt_at &lt;= now</c>. Колонки идут
/// в порядке селективности: обработанные строки отсекаются первыми.
/// </para>
/// <para>
/// Внешних ключей нет: outbox — инфраструктурная таблица, письмо переживает удаление
/// пользователя и токена, а получатель хранится снимком.
/// </para>
/// </remarks>
public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox", table =>
        {
            table.HasCheckConstraint(
                "ck_email_outbox_attempts",
                "attempts >= 0");

            // Отправленная и окончательно проваленная строка — взаимоисключающие состояния.
            table.HasCheckConstraint(
                "ck_email_outbox_state",
                "NOT (sent_at IS NOT NULL AND failed_at IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_email_outbox");

        // UUIDv7 генерирует домен (Entity..ctor) — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.DeduplicationKey)
            .HasColumnName("deduplication_key")
            .HasMaxLength(EmailOutboxMessage.MAX_DEDUPLICATION_KEY_LENGTH)
            .IsRequired();
        builder.Property(x => x.ToEmail)
            .HasColumnName("to_email")
            .HasMaxLength(EmailOutboxMessage.MAX_EMAIL_LENGTH)
            .IsRequired();
        builder.Property(x => x.Subject)
            .HasColumnName("subject")
            .HasMaxLength(EmailOutboxMessage.MAX_SUBJECT_LENGTH)
            .IsRequired();
        builder.Property(x => x.HtmlBody).HasColumnName("html_body").IsRequired();
        builder.Property(x => x.TextBody).HasColumnName("text_body").IsRequired();

        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.FailedAt).HasColumnName("failed_at");
        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(EmailOutboxMessage.MAX_LAST_ERROR_LENGTH);

        // Одно письмо — одна строка: ключ дедупликации уникален.
        builder.HasIndex(x => x.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("ux_email_outbox_deduplication_key");

        // Вход диспетчера: необработанные строки, созревшие к отправке, в порядке очереди.
        builder.HasIndex(x => new { x.SentAt, x.FailedAt, x.NextAttemptAt })
            .HasDatabaseName("ix_email_outbox_dispatch");
    }
}
