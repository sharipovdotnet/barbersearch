using BarberSearch.Application.Common;
using BarberSearch.Application.HairServices;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.Admin;
using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IHairServiceRepository"/> для тестов проверок ссылочной целостности,
/// чтения справочника и записи услуги.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>,
/// <see cref="GetActiveAsync"/>, <see cref="GetAdminListAsync"/>, <see cref="GetByIdAsync"/>,
/// <see cref="AddAsync"/> и <see cref="UpdateAsync"/>. Админский список фильтрует
/// и сортирует по тем же правилам, что и реальный репозиторий (включая поиск по названию
/// специализации), иначе юнит-тест врал бы о контракте.
/// </remarks>
public sealed class FakeHairServiceRepository : IHairServiceRepository
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly List<HairServiceCatalogRow> _active = [];
    private readonly List<AdminHairServiceResponse> _adminRows = [];
    private readonly Dictionary<Guid, HairService> _stored = [];

    /// <summary>Сбой чтения справочника: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetActiveFailure { get; set; }

    /// <summary>Сбой админского чтения: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetAdminListFailure { get; set; }

    /// <summary>Что обработчик передал в репозиторий: проверка разбора параметров.</summary>
    public AdminCatalogueStatus? LastStatus { get; private set; }

    /// <summary>Поиск после разбора обработчиком: <c>null</c>, если фильтра нет.</summary>
    public string? LastSearch { get; private set; }

    /// <summary>Услуги, добавленные через <see cref="AddAsync"/>: проверка записи.</summary>
    public List<HairService> Added { get; } = [];

    /// <summary>Услуги, сохранённые через <see cref="UpdateAsync"/>: проверка смены специализации.</summary>
    public List<HairService> Updated { get; } = [];

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddActive(HairService hairService, ServiceCategory category)
        => _active.Add(new HairServiceCatalogRow(hairService, category));

    public void AddAdminRow(AdminHairServiceResponse row) => _adminRows.Add(row);

    /// <summary>Кладёт услугу в хранилище: её вернёт <see cref="GetByIdAsync"/>.</summary>
    public void Store(HairService hairService) => _stored[hairService.Id] = hairService;

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        if (GetActiveFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>>(failure);
        }

        return Task.FromResult<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>>(_active);
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

            // Как и реальный запрос: название услуги ИЛИ название специализации.
            rows = rows.Where(row =>
                row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (row.SpecializationName?.Contains(
                        needle,
                        StringComparison.OrdinalIgnoreCase)
                    ?? false));
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
    {
        if (_stored.TryGetValue(id, out var hairService))
        {
            return Task.FromResult<Result<HairService, Failure>>(hairService);
        }

        return Task.FromResult<Result<HairService, Failure>>(new Failure(Error.NotFound(
            ApiErrorCodes.HairService.NotFound,
            $"Hair service '{id}' was not found.")));
    }

    public Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>>(
            _active.Where(row => row.Category == category).ToList());

    public Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
    {
        Added.Add(hairService);
        Store(hairService);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
    {
        Updated.Add(hairService);
        Store(hairService);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> DeleteAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
