using BarberSearch.Application.Common;
using BarberSearch.Application.HairServices;
using BarberSearch.Contracts.HairServices.Admin;
using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IHairServiceRepository"/> для тестов проверок ссылочной целостности
/// и чтения справочника.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>,
/// <see cref="GetActiveAsync"/> и <see cref="GetAdminListAsync"/>. Админский список фильтрует
/// и сортирует по тем же правилам, что и реальный репозиторий, иначе юнит-тест врал бы
/// о контракте.
/// </remarks>
public sealed class FakeHairServiceRepository : IHairServiceRepository
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly List<HairService> _active = [];
    private readonly List<AdminHairServiceResponse> _adminRows = [];

    /// <summary>Сбой чтения справочника: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetActiveFailure { get; set; }

    /// <summary>Сбой админского чтения: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetAdminListFailure { get; set; }

    /// <summary>Что обработчик передал в репозиторий: проверка разбора параметров.</summary>
    public AdminCatalogueStatus? LastStatus { get; private set; }

    /// <summary>Поиск после разбора обработчиком: <c>null</c>, если фильтра нет.</summary>
    public string? LastSearch { get; private set; }

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddActive(HairService hairService) => _active.Add(hairService);

    public void AddAdminRow(AdminHairServiceResponse row) => _adminRows.Add(row);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        if (GetActiveFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<HairService>, Failure>>(failure);
        }

        return Task.FromResult<Result<IReadOnlyList<HairService>, Failure>>(_active);
    }

    public Task<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        LastStatus = status;
        LastSearch = search;

        if (GetAdminListFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>>(
                failure);
        }

        IEnumerable<AdminHairServiceResponse> rows = _adminRows;

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
            .OrderBy(row => row.Category)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id)
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>>(ordered);
    }

    public Task<Result<HairService, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> UpdateAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> DeleteAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
