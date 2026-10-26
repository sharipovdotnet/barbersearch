using System.Reflection;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IUserRepository"/> для тестов обработчиков.
/// </summary>
public sealed class FakeUserRepository : IUserRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, User> _users = [];
    private readonly HashSet<Guid> _existingIds = [];
    private Dictionary<Guid, User>? _snapshot;

    public IReadOnlyCollection<User> Users => _users.Values;

    /// <summary>
    /// Сколько раз обработчики загружали агрегат через <see cref="GetByIdAsync"/>.
    /// </summary>
    public int GetByIdCallCount { get; private set; }

    /// <summary>
    /// Сколько раз обработчики читали узкую проекцию через <see cref="GetSecurityStampAsync"/>.
    /// </summary>
    public int GetSecurityStampCallCount { get; private set; }

    /// <summary>
    /// Сколько раз обработчики сохраняли изменения агрегата через <see cref="UpdateAsync"/>.
    /// </summary>
    public int UpdateCallCount { get; private set; }

    /// <summary>
    /// Подмена сохранения пользователя: тест проверяет откат транзакции при ошибке записи.
    /// </summary>
    public Func<User, UnitResult<Failure>>? UpdateBehavior { get; set; }

    public void Add(User user) => _users[user.Id] = user;

    public void BeginTransaction()
        => _snapshot = _users.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _users.Clear();

        foreach (var entry in _snapshot)
        {
            _users[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    /// <summary>
    /// Помечает идентификатор существующим без построения агрегата — для проверок,
    /// которым нужен только ответ <c>ExistsAsync</c>.
    /// </summary>
    public void AddExisting(Guid id) => _existingIds.Add(id);

    public Task<Result<User, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        GetByIdCallCount++;

        return Task.FromResult<Result<User, Failure>>(
            _users.TryGetValue(id, out var user)
                ? user
                : new Failure(Error.NotFound(
                    ApiErrorCodes.User.NotFound,
                    $"User '{id}' was not found.",
                    id)));
    }

    /// <summary>
    /// Узкая проекция без построения агрегата — как в PostgreSQL-репозитории (R-11).
    /// </summary>
    public Task<Result<UserSecurityStamp, Failure>> GetSecurityStampAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        GetSecurityStampCallCount++;

        if (!_users.TryGetValue(id, out var user))
        {
            return Task.FromResult<Result<UserSecurityStamp, Failure>>(new Failure(
                Error.NotFound(
                    ApiErrorCodes.User.SecurityStampNotFound,
                    $"User '{id}' was not found.",
                    id)));
        }

        return Task.FromResult<Result<UserSecurityStamp, Failure>>(
            new UserSecurityStamp(user.IsActive, user.SecurityStamp));
    }

    public Task<Result<User, Failure>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalized = User.NormalizeEmail(email);
        var user = _users.Values.FirstOrDefault(x => x.Email == normalized);

        if (user is null)
        {
            return Task.FromResult<Result<User, Failure>>(new Failure(Error.NotFound(
                ApiErrorCodes.User.NotFound,
                $"User with email '{email}' was not found.")));
        }

        return Task.FromResult<Result<User, Failure>>(user);
    }

    public Task<Result<IReadOnlyList<User>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<bool, Failure>> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalized = User.NormalizeEmail(email);

        return Task.FromResult<Result<bool, Failure>>(
            _users.Values.Any(x => x.Email == normalized));
    }

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(
            _existingIds.Contains(id) || _users.ContainsKey(id));

    public Task<UnitResult<Failure>> AddAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        _users[user.Id] = user;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;

        return Task.FromResult(
            UpdateBehavior?.Invoke(user) ?? UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> DeleteAsync(
        User user,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// Копия агрегата с тем же <c>Id</c>: обработчик мутирует загруженный экземпляр,
    /// а «таблица» должна вернуться к снимку при откате транзакции.
    /// </summary>
    private static User Clone(User user)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (User)memberwiseClone.Invoke(user, null)!;
    }
}
