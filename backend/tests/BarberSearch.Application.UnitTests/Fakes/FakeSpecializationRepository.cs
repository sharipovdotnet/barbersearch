using BarberSearch.Application.Common;
using BarberSearch.Application.Specializations;
using BarberSearch.Contracts.Specializations.Admin;
using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="ISpecializationRepository"/> для тестов проверок ссылочной целостности
/// и чтения справочника.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>,
/// <see cref="GetActiveAsync"/> и <see cref="GetAdminListAsync"/>. Админский список фильтрует
/// и сортирует по тем же правилам, что и реальный репозиторий.
/// </remarks>
public sealed class FakeSpecializationRepository : ISpecializationRepository
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly List<Specialization> _active = [];
    private readonly List<AdminSpecializationResponse> _adminRows = [];

    /// <summary>Сбой чтения справочника: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetActiveFailure { get; set; }

    /// <summary>Сбой админского чтения: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetAdminListFailure { get; set; }

    /// <summary>Что обработчик передал в репозиторий: проверка разбора параметров.</summary>
    public AdminCatalogueStatus? LastStatus { get; private set; }

    /// <summary>Поиск после разбора обработчиком: <c>null</c>, если фильтра нет.</summary>
    public string? LastSearch { get; private set; }

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddActive(Specialization specialization) => _active.Add(specialization);

    public void AddAdminRow(AdminSpecializationResponse row) => _adminRows.Add(row);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<IReadOnlyList<Specialization>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        if (GetActiveFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<Specialization>, Failure>>(failure);
        }

        return Task.FromResult<Result<IReadOnlyList<Specialization>, Failure>>(_active);
    }

    public Task<Result<IReadOnlyList<AdminSpecializationResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        LastStatus = status;
        LastSearch = search;

        if (GetAdminListFailure is { } failure)
        {
            return Task.FromResult<Result<
                IReadOnlyList<AdminSpecializationResponse>,
                Failure>>(failure);
        }

        IEnumerable<AdminSpecializationResponse> rows = _adminRows;

        rows = status switch
        {
            AdminCatalogueStatus.Active => rows.Where(row => row.IsActive),
            AdminCatalogueStatus.Hidden => rows.Where(row => !row.IsActive),
            _ => rows
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();

            rows = rows.Where(row =>
                row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = rows
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id)
            .ToList();

        return Task.FromResult<Result<
            IReadOnlyList<AdminSpecializationResponse>,
            Failure>>(ordered);
    }

    public Task<Result<Specialization, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<Specialization, Failure>> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<Specialization>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> UpdateAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> DeleteAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
