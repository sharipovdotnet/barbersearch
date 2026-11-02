using System.Text.Json;
using BarberSearch.Application.Audit;
using BarberSearch.Application.Audit.Admin;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Чтение журнала изменений из <c>audit_log</c>.
/// </summary>
/// <remarks>
/// <para>
/// Слой Application не видит инфраструктурный <c>AuditLogEntry</c>: запрос проецируется
/// в плоскую строку, а <c>payload</c> разбирается здесь же и превращается в контракт
/// <see cref="AdminAuditEntryResponse"/>. Автор подтягивается <c>LEFT JOIN</c> по
/// <c>users</c>: запись без автора или с удалённым аккаунтом остаётся в списке
/// с <c>actor.email = null</c>, а не выпадает из него.
/// </para>
/// <para>
/// Порядок — <c>occurred_at DESC, id</c>. Второй ключ обязателен: строки одного
/// <c>SaveChanges</c> имеют одинаковый <c>occurred_at</c>, и без него keyset-страницы
/// теряли бы или дублировали записи.
/// </para>
/// </remarks>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public AuditLogRepository(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AdminAuditEntryResponse>, Failure>> GetAdminPageAsync(
        AdminAuditFilters filters,
        AdminAuditCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminAudit.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminAuditQuery(
                _context,
                filters.EntityType,
                filters.Actor,
                filters.OccurredFrom,
                filters.OccurredToExclusive,
                cursor,
                limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AdminAuditEntryResponse(
                row.Id,
                row.OccurredAt,
                row.Action,
                row.EntityType,
                row.EntityId,
                row.ActorUserId is { } actorId
                    ? new AdminAuditActorResponse(actorId, row.ActorEmail)
                    : null,
                ParseChanges(row.Payload)))
            .ToList();
    }

    /// <summary>
    /// Собирает SQL-запрос списка: фильтры, keyset, порядок и проекция в плоскую строку.
    /// </summary>
    /// <remarks>
    /// Фильтр по email автора — <c>ILIKE</c> с экранированными <c>%</c>, <c>_</c> и <c>\</c>:
    /// «%» в запросе — литерал, а не «найти всё». Соединение с <c>users</c> левое, поэтому
    /// без фильтра видны и системные записи, и записи удалённых авторов.
    /// </remarks>
    private static IQueryable<AdminAuditRow> BuildAdminAuditQuery(
        ApplicationDbContext context,
        AdminAuditEntityType entityType,
        string? actor,
        DateTimeOffset? occurredFrom,
        DateTimeOffset? occurredToExclusive,
        AdminAuditCursor? cursor,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(context);

        var audits = context.AuditLogs.AsNoTracking();

        if (entityType != AdminAuditEntityType.All)
        {
            var storageName = AdminAuditEntityTypes.ToStorageName(entityType);
            audits = audits.Where(audit => audit.EntityType == storageName);
        }

        if (occurredFrom is { } from)
        {
            audits = audits.Where(audit => audit.OccurredAt >= from);
        }

        if (occurredToExclusive is { } toExclusive)
        {
            audits = audits.Where(audit => audit.OccurredAt < toExclusive);
        }

        // null — фильтр не задан; константное условие EF сворачивает целиком.
        var actorPattern = actor is null ? null : LikePattern.Contains(actor);

        // Левое соединение, а не внутреннее: системные записи и записи удалённых авторов
        // обязаны остаться в списке (actor.email = null). Плоская проекция — до фильтрации:
        // свойства анонимного типа EF переводит в SQL без materialзации сущностей.
        var joined =
            from audit in audits
            join candidate in context.Users.AsNoTracking()
                on audit.ActorUserId equals (Guid?)candidate.Id into actors
            from actorUser in actors.DefaultIfEmpty()
            where actorPattern == null
                || (actorUser != null
                    && EF.Functions.ILike(actorUser.Email, actorPattern!, "\\"))
            select new
            {
                audit.Id,
                audit.OccurredAt,
                audit.Action,
                audit.EntityType,
                audit.EntityId,
                audit.ActorUserId,
                ActorEmail = actorUser != null ? actorUser.Email : null,
                audit.Payload
            };

        if (cursor is { } position)
        {
            joined = joined.Where(row =>
                row.OccurredAt < position.OccurredAt
                || (row.OccurredAt == position.OccurredAt
                    && row.Id.CompareTo(position.Id) > 0));
        }

        return joined
            .OrderByDescending(row => row.OccurredAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .Select(row => new AdminAuditRow(
                row.Id,
                row.OccurredAt,
                row.Action,
                row.EntityType,
                row.EntityId,
                row.ActorUserId,
                row.ActorEmail,
                row.Payload));
    }

    /// <summary>
    /// Разбирает <c>payload</c> строки в типизированные изменения.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Две известные формы: <c>{"changes":{field:{from,to}}}</c> для правки и
    /// <c>{"values":{field:value}}</c> для создания и удаления. Поля сортируются
    /// по имени (Ordinal): <c>jsonb</c> не сохраняет порядок ключей, и без сортировки
    /// порядок diff'а зависел бы от внутреннего представления PostgreSQL.
    /// </para>
    /// <para>
    /// Неизвестная форма и битый JSON дают пустой список, а не исключение: строка, записанная
    /// вчерашней версией кода или руками, не должна ронять чтение всего журнала.
    /// </para>
    /// </remarks>
    private static List<AdminAuditFieldChangeResponse> ParseChanges(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (root.TryGetProperty("changes", out var changes)
                && changes.ValueKind == JsonValueKind.Object)
            {
                return changes
                    .EnumerateObject()
                    .Where(change => change.Value.ValueKind == JsonValueKind.Object)
                    .OrderBy(change => change.Name, StringComparer.Ordinal)
                    .Select(change => new AdminAuditFieldChangeResponse(
                        change.Name,
                        ReadScalar(change.Value, "from"),
                        ReadScalar(change.Value, "to")))
                    .ToList();
            }

            if (root.TryGetProperty("values", out var values)
                && values.ValueKind == JsonValueKind.Object)
            {
                return values
                    .EnumerateObject()
                    .Where(value => IsScalar(value.Value))
                    .OrderBy(value => value.Name, StringComparer.Ordinal)
                    .Select(value => new AdminAuditFieldChangeResponse(
                        value.Name,
                        null,
                        value.Value.Clone()))
                    .ToList();
            }

            return [];
        }
    }

    /// <summary>
    /// Читает скаляр из объекта: отсутствующее или <c>null</c> свойство — <c>null</c>,
    /// объект и массив — тоже <c>null</c> (контракт обещает только скаляры).
    /// </summary>
    private static JsonElement? ReadScalar(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || !IsScalar(value))
        {
            return null;
        }

        return value.Clone();
    }

    private static bool IsScalar(JsonElement value)
        => value.ValueKind is JsonValueKind.String
            or JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False;

    /// <summary>
    /// Плоская строка запроса: инфраструктурный <c>AuditLogEntry</c> не покидает репозиторий.
    /// </summary>
    private sealed record AdminAuditRow(
        Guid Id,
        DateTimeOffset OccurredAt,
        string Action,
        string EntityType,
        Guid EntityId,
        Guid? ActorUserId,
        string? ActorEmail,
        string Payload);
}
