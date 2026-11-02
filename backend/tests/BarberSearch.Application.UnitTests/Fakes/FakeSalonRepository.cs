using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="ISalonRepository"/> для тестов проверок ссылочной целостности
/// и админского чтения справочника.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>
/// и <see cref="GetAdminListAsync"/>. Остальные методы намеренно падают, чтобы тест не прошёл
/// случайно по неверной ветке. Админский список фильтрует и сортирует по тем же правилам,
/// что и реальный репозиторий (город, название, идентификатор).
/// </remarks>
public sealed class FakeSalonRepository : ISalonRepository
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly List<AdminSalonResponse> _adminRows = [];

    /// <summary>Сбой админского чтения: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetAdminListFailure { get; set; }

    /// <summary>Что обработчик передал в репозиторий: проверка разбора параметров.</summary>
    public AdminCatalogueStatus? LastStatus { get; private set; }

    /// <summary>Поиск после разбора обработчиком: <c>null</c>, если фильтра нет.</summary>
    public string? LastSearch { get; private set; }

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddAdminRow(AdminSalonResponse row) => _adminRows.Add(row);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<IReadOnlyList<AdminSalonResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        LastStatus = status;
        LastSearch = search;

        if (GetAdminListFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminSalonResponse>, Failure>>(failure);
        }

        IEnumerable<AdminSalonResponse> rows = _adminRows;

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
                row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.City.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = rows
            .OrderBy(row => row.City, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id)
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<AdminSalonResponse>, Failure>>(ordered);
    }

    public Task<Result<Salon, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<Salon>, Failure>> GetByCityAsync(
        string city,
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<Salon>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> UpdateAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> DeleteAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
