using System.Reflection;
using BarberSearch.Application.BarberRequests;
using BarberSearch.Application.BarberRequests.Admin;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberRequestRepository"/> для тестов обработчиков.
/// </summary>
/// <remarks>
/// Повторяет то, что важно обработчикам: «последняя заявка», «есть ли <c>Pending</c>» и
/// конфликт частичного уникального индекса при повторной подаче. Административный список
/// задаётся строками через <see cref="AddAdminRow"/>: порядок и фильтр проверяются по ним,
/// а не по времени создания агрегата в памяти.
/// </remarks>
public sealed class FakeBarberRequestRepository
    : IBarberRequestRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, BarberRequest> _requests = [];
    private readonly List<AdminBarberRequestResponse> _adminRows = [];
    private Dictionary<Guid, BarberRequest>? _snapshot;

    public IReadOnlyCollection<BarberRequest> Requests => _requests.Values;

    /// <summary>
    /// Сколько раз обработчик пытался сохранить новую заявку.
    /// </summary>
    public int AddCallCount { get; private set; }

    /// <summary>
    /// Сколько раз обработчик сохранял решение по заявке.
    /// </summary>
    public int UpdateCallCount { get; private set; }

    /// <summary>
    /// Подмена сохранения заявки: так проверяется обработка гонки на уникальном индексе.
    /// </summary>
    public Func<BarberRequest, UnitResult<Failure>>? AddBehavior { get; set; }

    /// <summary>
    /// Подмена сохранения решения: тест проверяет откат транзакции, когда запись падает
    /// уже после мутации агрегата.
    /// </summary>
    public Func<BarberRequest, UnitResult<Failure>>? UpdateBehavior { get; set; }

    /// <summary>Ответ <see cref="CountPendingAsync"/>: сколько заявок в очереди.</summary>
    public int PendingCount { get; set; }

    /// <summary>Подмена ошибки счётчика заявок в очереди.</summary>
    public Failure? CountPendingFailure { get; set; }

    /// <summary>Сколько раз сводка читала счётчик заявок в очереди.</summary>
    public int CountPendingCallCount { get; private set; }

    /// <summary>
    /// Хук перед счётчиком заявок: тест сводки проверяет порядок последовательных чтений.
    /// </summary>
    public Action? BeforeCountPending { get; set; }

    public void Add(BarberRequest request) => _requests[request.Id] = request;

    /// <summary>
    /// Добавляет готовую строку административного списка.
    /// </summary>
    public void AddAdminRow(AdminBarberRequestResponse row) => _adminRows.Add(row);

    public void BeginTransaction()
        => _snapshot = _requests.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _requests.Clear();

        foreach (var entry in _snapshot)
        {
            _requests[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    public Task<Result<BarberRequest, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<BarberRequest, Failure>>(
            _requests.TryGetValue(id, out var request)
                ? request
                : new Failure(Error.NotFound(
                    ApiErrorCodes.BarberRequest.NotFound,
                    $"Barber request '{id}' was not found.",
                    id)));
    }

    public Task<Result<BarberRequestResponse?, Failure>> GetLatestByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var request = _requests.Values
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        return Task.FromResult<Result<BarberRequestResponse?, Failure>>(
            request is null
                ? Result.Success<BarberRequestResponse?, Failure>(null)
                : BarberRequestResponseMapper.Map(request));
    }

    public Task<Result<bool, Failure>> HasPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(
            _requests.Values.Any(x =>
                x.UserId == userId && x.Status == BarberRequestStatus.Pending));

    public Task<Result<IReadOnlyList<AdminBarberRequestResponse>, Failure>> GetAdminPageAsync(
        AdminBarberRequestStatus status,
        AdminBarberRequestCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminBarberRequestResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.BarberRequest.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        IEnumerable<AdminBarberRequestResponse> query = _adminRows;

        if (status == AdminBarberRequestStatus.Pending)
        {
            query = query.Where(row => row.Status == BarberRequestStatus.Pending);
        }

        if (cursor is { } position)
        {
            query = status == AdminBarberRequestStatus.Pending
                ? query.Where(row =>
                    row.CreatedAt > position.CreatedAt
                    || (row.CreatedAt == position.CreatedAt
                        && row.Id.CompareTo(position.Id) > 0))
                : query.Where(row =>
                    row.CreatedAt < position.CreatedAt
                    || (row.CreatedAt == position.CreatedAt
                        && row.Id.CompareTo(position.Id) > 0));
        }

        var rows = (status == AdminBarberRequestStatus.Pending
                ? query.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id)
                : query.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id))
            .Take(limit)
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<AdminBarberRequestResponse>, Failure>>(rows);
    }

    /// <summary>
    /// Счётчик заявок в очереди; тест задаёт значение и ошибку явно.
    /// </summary>
    public Task<Result<int, Failure>> CountPendingAsync(
        CancellationToken cancellationToken = default)
    {
        CountPendingCallCount++;
        BeforeCountPending?.Invoke();

        return Task.FromResult(CountPendingFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(PendingCount));
    }

    public Task<Result<AdminBarberRequestResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = _adminRows.FirstOrDefault(x => x.Id == id);

        return Task.FromResult<Result<AdminBarberRequestResponse, Failure>>(
            row is null
                ? new Failure(Error.NotFound(
                    ApiErrorCodes.BarberRequest.NotFound,
                    $"Barber request '{id}' was not found.",
                    id))
                : row);
    }

    public Task<UnitResult<Failure>> AddAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default)
    {
        AddCallCount++;

        if (AddBehavior is { } behavior)
        {
            return Task.FromResult(behavior(request));
        }

        // Аналог частичного уникального индекса ux_barber_requests_pending_user.
        if (_requests.Values.Any(x =>
                x.UserId == request.UserId && x.Status == BarberRequestStatus.Pending))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                ApiErrorCodes.BarberRequest.AddUniqueViolation,
                "A pending barber request already exists for this user."))));
        }

        _requests[request.Id] = request;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;

        if (UpdateBehavior is { } behavior)
        {
            return Task.FromResult(behavior(request));
        }

        _requests[request.Id] = request;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    /// <summary>
    /// Копия агрегата с тем же <c>Id</c>: обработчик мутирует загруженный экземпляр,
    /// а «таблица» должна вернуться к снимку при откате транзакции.
    /// </summary>
    private static BarberRequest Clone(BarberRequest request)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (BarberRequest)memberwiseClone.Invoke(request, null)!;
    }
}
