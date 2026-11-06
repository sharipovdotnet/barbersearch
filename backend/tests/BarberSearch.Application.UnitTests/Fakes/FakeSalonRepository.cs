using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="ISalonRepository"/> для тестов проверок ссылочной целостности,
/// админского чтения справочника и модерации салонов.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>,
/// <see cref="GetByIdAsync"/>, <see cref="AddAsync"/>, <see cref="UpdateAsync"/>
/// и <see cref="GetAdminListAsync"/>. Остальные методы намеренно падают, чтобы тест не прошёл
/// случайно по неверной ветке. Админский список фильтрует и сортирует по тем же правилам,
/// что и реальный репозиторий (город, название, идентификатор).
/// <para>
/// Как участник <see cref="FakeUnitOfWork"/> репозиторий снимает снимок словаря салонов:
/// так тест видит, что создание и привязка к профилю фиксируются одной транзакцией.
/// </para>
/// </remarks>
public sealed class FakeSalonRepository : ISalonRepository, IFakeTransactionParticipant
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly Dictionary<Guid, Salon> _salons = [];
    private readonly List<AdminSalonResponse> _adminRows = [];
    private Dictionary<Guid, Salon>? _snapshot;
    private HashSet<Guid>? _existingIdsSnapshot;

    /// <summary>Сбой админского чтения: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetAdminListFailure { get; set; }

    /// <summary>Что обработчик передал в репозиторий: проверка разбора параметров.</summary>
    public AdminCatalogueStatus? LastStatus { get; private set; }

    /// <summary>Поиск после разбора обработчиком: <c>null</c>, если фильтра нет.</summary>
    public string? LastSearch { get; private set; }

    /// <summary>Город каталога после разбора обработчиком.</summary>
    public string? LastDirectoryCity { get; private set; }

    /// <summary>Поиск каталога после разбора обработчиком.</summary>
    public string? LastDirectorySearch { get; private set; }

    /// <summary>Ответ <see cref="CountInactiveAsync"/>: выключенных салонов в сценарии.</summary>
    public int InactiveCount { get; set; }

    /// <summary>Подмена ошибки счётчика выключенных салонов.</summary>
    public Failure? CountInactiveFailure { get; set; }

    /// <summary>Сколько раз обработчик вызывал <see cref="AddAsync"/>.</summary>
    public int AddCalls { get; private set; }

    /// <summary>Сколько раз обработчик вызывал <see cref="UpdateAsync"/>.</summary>
    public int UpdateCalls { get; private set; }

    /// <summary>Салоны, сохранённые через <see cref="AddAsync"/> или <see cref="UpdateAsync"/>.</summary>
    public IReadOnlyCollection<Salon> Salons => _salons.Values;

    /// <summary>Подмена вставки салона: тест проверяет откат транзакции.</summary>
    public Func<Salon, UnitResult<Failure>>? AddBehavior { get; set; }

    /// <summary>Подмена сохранения салона: тест проверяет откат транзакции.</summary>
    public Func<Salon, UnitResult<Failure>>? UpdateBehavior { get; set; }

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddSalon(Salon salon)
    {
        _salons[salon.Id] = salon;
        _existingIds.Add(salon.Id);
    }

    public void AddAdminRow(AdminSalonResponse row) => _adminRows.Add(row);

    public void BeginTransaction()
    {
        _snapshot = new Dictionary<Guid, Salon>(_salons);
        _existingIdsSnapshot = [.. _existingIds];
    }

    public void CommitTransaction()
    {
        _snapshot = null;
        _existingIdsSnapshot = null;
    }

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _salons.Clear();

        foreach (var entry in _snapshot)
        {
            _salons[entry.Key] = entry.Value;
        }

        _existingIds.Clear();
        _existingIds.UnionWith(_existingIdsSnapshot!);

        _snapshot = null;
        _existingIdsSnapshot = null;
    }

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<int, Failure>> CountInactiveAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(CountInactiveFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(InactiveCount));

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
            AdminCatalogueStatus.Active => rows.Where(row =>
                row.IsActive && row.ModerationStatus == SalonModerationStatus.Active),
            AdminCatalogueStatus.Hidden => rows.Where(row =>
                !row.IsActive && row.ModerationStatus == SalonModerationStatus.Active),
            AdminCatalogueStatus.Pending => rows.Where(row =>
                row.ModerationStatus == SalonModerationStatus.Pending),
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

    public Task<Result<IReadOnlyList<SalonDirectoryItemResponse>, Failure>> GetDirectoryAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default)
    {
        LastDirectoryCity = city;
        LastDirectorySearch = search;

        IEnumerable<Salon> rows = _salons.Values.Where(salon =>
            salon.IsActive
            && salon.IsListed
            && salon.ModerationStatus == SalonModerationStatus.Active);

        if (city is not null)
        {
            rows = rows.Where(salon => salon.City == city);
        }

        if (search is not null)
        {
            rows = rows.Where(salon =>
                salon.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || salon.Address.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var projected = rows
            .OrderBy(salon => salon.City, StringComparer.Ordinal)
            .ThenBy(salon => salon.Name, StringComparer.Ordinal)
            .ThenBy(salon => salon.Id)
            .Select(salon => new SalonDirectoryItemResponse(
                salon.Id,
                salon.Name,
                salon.City,
                salon.Address,
                salon.TimeZoneId,
                salon.Kind))
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<SalonDirectoryItemResponse>, Failure>>(
            projected);
    }

    public Task<Result<Salon, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<Salon, Failure>>(
            _salons.TryGetValue(id, out var salon)
                ? salon
                : new Failure(Error.NotFound(
                    ApiErrorCodes.Salon.NotFound,
                    $"Salon '{id}' was not found.",
                    id)));

    public Task<Result<IReadOnlyList<Salon>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<IReadOnlyList<Salon>, Failure>>(
            ids
                .Distinct()
                .Where(_salons.ContainsKey)
                .Select(id => _salons[id])
                .ToList());

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
    {
        AddCalls++;

        var result = AddBehavior?.Invoke(salon) ?? UnitResult.Success<Failure>();

        if (result.IsSuccess)
        {
            _salons[salon.Id] = salon;
            _existingIds.Add(salon.Id);
        }

        return Task.FromResult(result);
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        var result = UpdateBehavior?.Invoke(salon) ?? UnitResult.Success<Failure>();

        if (result.IsSuccess)
        {
            _salons[salon.Id] = salon;
            _existingIds.Add(salon.Id);
        }

        return Task.FromResult(result);
    }

    public Task<UnitResult<Failure>> DeleteAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
