using BarberSearch.Infrastructure.Postgresql.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>audit_log</c> — неизменяемой истории изменений (R-34).
/// </summary>
/// <remarks>
/// <para>
/// Внешнего ключа на <c>users</c> нет: аудит должен переживать удаление пользователя.
/// <c>actor_user_id</c> — исторический идентификатор, а не ссылка; удаление аккаунта
/// не должно ни каскадно удалять историю, ни запрещать её из-за <c>RESTRICT</c>.
/// </para>
/// <para>
/// Индексы закрывают три вопроса при разборе инцидента: «что произошло за период»
/// (<c>occurred_at DESC, id</c> — ведущая колонка даёт и период, и keyset-порядок админской
/// ленты), «что происходило с этой сущностью» (<c>entity_type, entity_id</c>) и «что делал
/// этот пользователь» (<c>actor_user_id</c>).
/// </para>
/// </remarks>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(x => x.Id).HasName("pk_audit_log");

        // UUIDv7 генерирует AuditTrail — база значение не подставляет.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(AuditLogEntry.MAX_ACTION_LENGTH)
            .IsRequired();
        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(AuditLogEntry.MAX_ENTITY_TYPE_LENGTH)
            .IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        // Ключ админской ленты: свежие сверху, внутри одной секунды порядок задаёт id.
        // Составной индекс покрывает и «что произошло за период»: occurred_at — его
        // ведущая колонка. Отдельный ix_audit_log_occurred_at после замера удалён:
        // EXPLAIN (ANALYZE, BUFFERS) показывал Incremental Sort на (occurred_at, id).
        builder.HasIndex(x => new { x.OccurredAt, x.Id })
            .IsDescending(true, false)
            .HasDatabaseName("ix_audit_log_occurred_at_id");

        // История одной сущности: entity_type + entity_id.
        builder.HasIndex(x => new { x.EntityType, x.EntityId })
            .HasDatabaseName("ix_audit_log_entity_type_entity_id");

        // Действия конкретного пользователя.
        builder.HasIndex(x => x.ActorUserId)
            .HasDatabaseName("ix_audit_log_actor_user_id");
    }
}
