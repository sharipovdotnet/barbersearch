using System.Globalization;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Audit.Admin.GetAdminAuditLog;

/// <summary>
/// Обработчик админского журнала изменений: разбор фильтров, период в UTC-сутках,
/// страница свежими сверху и курсор продолжения.
/// </summary>
/// <remarks>
/// Журнал только читается: ни команды, ни доменной операции над <c>audit_log</c> нет.
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>: обработчик не знает, кто его
/// вызвал, и не принимает решений об авторизации.
/// <para>
/// Период включителен по обеим датам и трактуется как UTC-сутки:
/// <c>from</c> — <c>00:00Z</c> этого дня, <c>to</c> — <c>00:00Z</c> следующего дня.
/// </para>
/// </remarks>
public sealed class GetAdminAuditLogHandler
    : IQueryHandler<PagedResponse<AdminAuditEntryResponse>, GetAdminAuditLogQuery>
{
    /// <summary>
    /// Предел длины подстроки email автора: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_ACTOR_SEARCH_LENGTH = 100;

    /// <summary>
    /// Единственный принимаемый формат даты — UTC-сутки без времени и без локальных форматов.
    /// </summary>
    private const string DATE_FORMAT = "yyyy-MM-dd";

    private readonly IAuditLogRepository _auditLogRepository;

    public GetAdminAuditLogHandler(IAuditLogRepository auditLogRepository)
    {
        ArgumentNullException.ThrowIfNull(auditLogRepository);

        _auditLogRepository = auditLogRepository;
    }

    /// <summary>
    /// Возвращает страницу журнала с фильтрами типа, автора и периода.
    /// </summary>
    public async Task<Result<PagedResponse<AdminAuditEntryResponse>, Failure>> Handle(
        GetAdminAuditLogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!AdminAuditEntityTypes.TryParse(query.EntityType, out var entityType))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminAudit.EntityTypeInvalid,
                "Entity type must be one of 'all', 'user', 'barber_profile' or 'review'.",
                nameof(query.EntityType)));
        }

        var actor = query.Actor?.Trim();

        if (actor is { Length: > MAX_ACTOR_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminAudit.ActorTooLong,
                $"Actor must not be longer than {MAX_ACTOR_SEARCH_LENGTH} characters.",
                nameof(query.Actor)));
        }

        if (string.IsNullOrEmpty(actor))
        {
            actor = null;
        }

        if (!TryParseDay(query.From, out var fromDay)
            || !TryParseDay(query.To, out var toDay))
        {
            return PeriodInvalid();
        }

        if (fromDay is { } first && toDay is { } last && first > last)
        {
            return PeriodInvalid();
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.AdminAudit.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminAuditCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminAuditCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var filters = new AdminAuditFilters(
            entityType,
            actor,
            StartOfUtcDay(fromDay),
            StartOfUtcDay(toDay?.AddDays(1)));

        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница,
        // не выполняя отдельный COUNT.
        var rowsResult = await _auditLogRepository.GetAdminPageAsync(
            filters,
            cursor,
            pageSize + 1,
            cancellationToken);
        if (rowsResult.IsFailure)
        {
            return rowsResult.Error;
        }

        var rows = rowsResult.Value;
        var hasNextPage = rows.Count > pageSize;
        var items = hasNextPage
            ? rows.Take(pageSize).ToList()
            : rows.ToList();

        var nextCursor = hasNextPage && items.Count > 0
            ? new AdminAuditCursor(items[^1].OccurredAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminAuditEntryResponse>(items, nextCursor);
    }

    /// <summary>
    /// Разбирает дату периода: пустая строка — «без границы», кривой формат — ошибка.
    /// </summary>
    private static bool TryParseDay(string? value, out DateOnly? day)
    {
        day = null;

        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                trimmed,
                DATE_FORMAT,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        day = parsed;

        return true;
    }

    /// <summary>
    /// Начало UTC-суток: дата без времени трактуется как <c>00:00Z</c>, а не в поясе сервера.
    /// </summary>
    private static DateTimeOffset? StartOfUtcDay(DateOnly? day)
        => day is { } value
            ? new DateTimeOffset(
                value.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero)
            : null;

    private static Failure PeriodInvalid()
        => new(Error.Validation(
            ApiErrorCodes.AdminAudit.PeriodInvalid,
            "Period must use the yyyy-MM-dd format and the start must not be after the end.",
            "from"));
}
