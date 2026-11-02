namespace BarberSearch.Contracts.Audit.Admin;

/// <summary>
/// Строка журнала изменений: когда, кто, что и с какой сущностью произошло.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Action"/> — <c>{entity}.{created|updated|deleted}</c>, например
/// <c>user.updated</c>. <see cref="EntityType"/> — стабильное имя из allowlist аудита
/// (<c>user</c>, <c>barber_profile</c>, <c>review</c>), а не имя CLR-типа.
/// </para>
/// <para>
/// Имя сущности намеренно не резолвится: <see cref="EntityId"/> — идентификатор изменённой
/// записи, которая могла быть переименована или удалена. «Актуальное» имя рядом с
/// историческим diff'ом вводило бы в заблуждение.
/// </para>
/// <para>
/// <see cref="Changes"/> — разобранный <c>payload</c>: поля отсортированы по имени, чтобы
/// порядок не зависел от внутреннего представления <c>jsonb</c>. Неизвестная форма payload
/// даёт пустой список, а не ошибку чтения.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор строки журнала.</param>
/// <param name="OccurredAt">Момент изменения в UTC.</param>
/// <param name="Action">Действие вида <c>{entity}.{created|updated|deleted}</c>.</param>
/// <param name="EntityType">Стабильное имя типа сущности: <c>user</c>, <c>barber_profile</c>, <c>review</c>.</param>
/// <param name="EntityId">Идентификатор изменённой сущности.</param>
/// <param name="Actor">Автор изменения; <c>null</c> для системной записи.</param>
/// <param name="Changes">Изменённые поля в детерминированном порядке.</param>
public record AdminAuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    Guid EntityId,
    AdminAuditActorResponse? Actor,
    IReadOnlyList<AdminAuditFieldChangeResponse> Changes);
