using BarberSearch.Application.Users;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IUserRepository"/> для тестов обработчиков.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = [];
    private readonly HashSet<Guid> _existingIds = [];

    public IReadOnlyCollection<User> Users => _users.Values;

    public void Add(User user) => _users[user.Id] = user;

    /// <summary>
    /// Помечает идентификатор существующим без построения агрегата — для проверок,
    /// которым нужен только ответ <c>ExistsAsync</c>.
    /// </summary>
    public void AddExisting(Guid id) => _existingIds.Add(id);

    public Task<Result<User, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<User, Failure>>(
            _users.TryGetValue(id, out var user)
                ? user
                : new Failure(Error.NotFound(
                    "user.getById.notFound",
                    $"User '{id}' was not found.",
                    id)));

    public Task<Result<User, Failure>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(x => x.Email == email);

        if (user is null)
        {
            return Task.FromResult<Result<User, Failure>>(new Failure(Error.NotFound(
                "user.getByEmail.notFound",
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
        => Task.FromResult<Result<bool, Failure>>(_users.Values.Any(x => x.Email == email));

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
        => Task.FromResult(UnitResult.Success<Failure>());

    public Task<UnitResult<Failure>> DeleteAsync(
        User user,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
