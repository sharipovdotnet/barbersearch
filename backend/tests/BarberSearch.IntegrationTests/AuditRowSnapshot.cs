namespace BarberSearch.IntegrationTests;

/// <summary>
/// Снимок строки журнала аудита для проверок покрытия.
/// </summary>
/// <remarks>
/// Тест сравнивает то, что записал <c>AuditTrail</c>: <c>action</c>, <c>entity_type</c>,
/// <c>entity_id</c>, автора и сырой <c>payload</c>. Отдельный тип, а не инфраструктурная
/// <c>AuditLogEntry</c>, чтобы проверки не зависели от EF-материализации.
/// </remarks>
/// <param name="Id">Идентификатор строки аудита.</param>
/// <param name="OccurredAt">Момент фиксации изменения.</param>
/// <param name="ActorUserId">Автор изменения; <c>null</c> — системная запись.</param>
/// <param name="Action">Действие вида <c>{entity}.{created|updated|deleted}</c>.</param>
/// <param name="EntityType">Стабильное имя типа сущности.</param>
/// <param name="EntityId">Идентификатор изменённой сущности.</param>
/// <param name="Payload">Сырой JSON с изменениями или значениями.</param>
internal sealed record AuditRowSnapshot(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid EntityId,
    string Payload);
