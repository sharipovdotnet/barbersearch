using BarberSearch.Application.Audit;
using BarberSearch.Application.Audit.Admin;
using BarberSearch.Contracts.Audit.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IAuditLogRepository"/> для тестов обработчика журнала.
/// </summary>
/// <remarks>
/// Повторяет контракт настоящего запроса: фильтры типа, автора и периода, порядок
/// <c>occurred_at DESC, id</c>, keyset и лимит. Тест поэтому проверяет поведение обработчика,
/// а не «заглушку, которая всегда возвращает всё».
/// </remarks>
public sealed class FakeAuditLogRepository : IAuditLogRepository
{
    private readonly List<AdminAuditEntryResponse> _rows = [];

    /// <summary>
    /// Фильтры последнего запроса: тест видит, что обработчик передал разобранные значения.
    /// </summary>
    public AdminAuditFilters? LastFilters { get; private set; }

    /// <summary>
    /// Курсор последнего запроса.
    /// </summary>
    public AdminAuditCursor? LastCursor { get; private set; }

    /// <summary>
    /// Лимит последнего запроса: обработчик читает <c>pageSize + 1</c> строку.
    /// </summary>
    public int LastLimit { get; private set; }

    /// <summary>
    /// Подмена результата: тест проверяет, что ошибка репозитория доходит до контроллера.
    /// </summary>
    public Failure? ForcedFailure { get; set; }

    /// <summary>
    /// Добавляет строку журнала.
    /// </summary>
    public void Add(AdminAuditEntryResponse row) => _rows.Add(row);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AdminAuditEntryResponse>, Failure>> GetAdminPageAsync(
        AdminAuditFilters filters,
        AdminAuditCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        LastFilters = filters;
        LastCursor = cursor;
        LastLimit = limit;

        if (ForcedFailure is not null)
        {
            return Task.FromResult(
                Result.Failure<IReadOnlyList<AdminAuditEntryResponse>, Failure>(ForcedFailure));
        }

        var storageName = filters.EntityType == AdminAuditEntityType.All
            ? null
            : AdminAuditEntityTypes.ToStorageName(filters.EntityType);

        var query = _rows.AsEnumerable();

        if (storageName is not null)
        {
            query = query.Where(row => row.EntityType == storageName);
        }

        if (filters.Actor is { } actor)
        {
            query = query.Where(row => row.Actor?.Email is { } email
                && email.Contains(actor, StringComparison.OrdinalIgnoreCase));
        }

        if (filters.OccurredFrom is { } from)
        {
            query = query.Where(row => row.OccurredAt >= from);
        }

        if (filters.OccurredToExclusive is { } toExclusive)
        {
            query = query.Where(row => row.OccurredAt < toExclusive);
        }

        if (cursor is { } position)
        {
            query = query.Where(row =>
                row.OccurredAt < position.OccurredAt
                || (row.OccurredAt == position.OccurredAt && row.Id.CompareTo(position.Id) > 0));
        }

        var page = query
            .OrderByDescending(row => row.OccurredAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminAuditEntryResponse>, Failure>(page));
    }
}
