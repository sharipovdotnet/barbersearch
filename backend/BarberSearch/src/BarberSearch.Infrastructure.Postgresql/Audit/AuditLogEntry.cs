namespace BarberSearch.Infrastructure.Postgresql.Audit;

/// <summary>
/// Неизменяемая строка аудита изменения доменной сущности (R-34).
/// </summary>
/// <remarks>
/// <para>
/// Строка аудита — не сущность домена: она не наследует <c>Entity</c>, у неё нет
/// <c>CreatedAt</c>/<c>UpdatedAt</c>, версионирования и методов изменения. После записи строка
/// только читается: «исправить» историю нельзя, для этого нет ни API, ни доменных операций.
/// </para>
/// <para>
/// <see cref="Payload"/> — JSON-документ в формате <c>jsonb</c>: для создания и удаления
/// <c>{"values":{…}}</c>, для изменения — <c>{"changes":{"role":{"from":"Customer","to":"Admin"}}}</c>.
/// Секреты (хеш пароля, security stamp) в открытом виде не пишутся — см. <c>AuditTrail</c>.
/// </para>
/// <para>
/// Внешнего ключа на <c>users</c> нет сознательно: аудит должен переживать удаление
/// пользователя, поэтому <see cref="ActorUserId"/> — просто идентификатор, а не ссылка.
/// </para>
/// </remarks>
public sealed class AuditLogEntry
{
    /// <summary>
    /// Предел длины <see cref="Action"/>: <c>{entity}.{created|updated|deleted}</c>
    /// укладывается в него с большим запасом.
    /// </summary>
    public const int MAX_ACTION_LENGTH = 64;

    /// <summary>
    /// Предел длины <see cref="EntityType"/>: стабильные имена <c>user</c>,
    /// <c>barber_profile</c>, <c>review</c>.
    /// </summary>
    public const int MAX_ENTITY_TYPE_LENGTH = 64;

    /// <summary>
    /// Предел длины одной строки внутри <see cref="Payload"/>: длинный текст отзыва
    /// обрезается, чтобы строка аудита не раздувала таблицу.
    /// </summary>
    public const int MAX_PAYLOAD_STRING_LENGTH = 500;

    /// <summary>Идентификатор строки аудита (UUIDv7 — вставки идут в порядке btree).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Момент фиксации изменения. Один и тот же для всех строк, собранных за один
    /// <c>SaveChanges</c>.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// Кто изменил: идентификатор текущего пользователя; <c>null</c> для анонимной операции.
    /// </summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>Что произошло: <c>user.created</c>, <c>review.updated</c> и т. п.</summary>
    public string Action { get; private set; } = null!;

    /// <summary>
    /// Тип сущности стабильным именем (<c>user</c>, <c>barber_profile</c>, <c>review</c>),
    /// а не CLR-именем: переименование класса не должно менять историю.
    /// </summary>
    public string EntityType { get; private set; } = null!;

    /// <summary>Идентификатор изменённой сущности.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>JSON с изменениями или значениями — см. примечание класса.</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>
    /// Пустой конструктор для материализации EF Core.
    /// </summary>
    private AuditLogEntry()
    {
    }

    /// <summary>
    /// Создаёт строку аудита. Вызывается только <c>AuditTrail</c> в момент сохранения
    /// изменений, поэтому фабрика внутренняя.
    /// </summary>
    /// <param name="occurredAt">Момент фиксации изменения.</param>
    /// <param name="actorUserId">Пользователь, от имени которого выполнено изменение.</param>
    /// <param name="action">Действие вида <c>{entity}.{created|updated|deleted}</c>.</param>
    /// <param name="entityType">Стабильное имя типа сущности.</param>
    /// <param name="entityId">Идентификатор изменённой сущности.</param>
    /// <param name="payload">JSON с изменениями или значениями.</param>
    internal static AuditLogEntry Create(
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        string payload)
    {
        return new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Payload = payload
        };
    }
}
