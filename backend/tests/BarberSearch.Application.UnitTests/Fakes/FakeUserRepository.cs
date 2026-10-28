using System.Reflection;
using BarberSearch.Application.Users;
using BarberSearch.Application.Users.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
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
    private readonly List<AdminUserResponse> _adminRows = [];
    private readonly HashSet<Guid> _existingIds = [];
    private readonly Dictionary<Guid, KnownBarberProfile> _barberProfiles = [];
    private Dictionary<Guid, User>? _snapshot;

    public IReadOnlyCollection<User> Users => _users.Values;

    /// <summary>
    /// Параметры последнего админского запроса страницы: тесты проверяют, что обработчик
    /// передал разобранные фильтры и увеличенный на единицу лимит.
    /// </summary>
    public AdminUserStatus? LastAdminStatus { get; private set; }

    /// <summary>
    /// Фильтр роли последнего админского запроса.
    /// </summary>
    public UserRole? LastAdminRole { get; private set; }

    /// <summary>
    /// Поисковая строка последнего админского запроса.
    /// </summary>
    public string? LastAdminSearch { get; private set; }

    /// <summary>
    /// Лимит последнего админского запроса.
    /// </summary>
    public int? LastAdminLimit { get; private set; }

    /// <summary>
    /// Сколько раз обработчик брал блокировку администраторов.
    /// </summary>
    public int AcquireAdminLockCallCount { get; private set; }

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

    /// <summary>
    /// Добавляет строку админского списка: порядок и фильтры проверяются по ней,
    /// а не по времени создания агрегата в памяти.
    /// </summary>
    public void AddAdminRow(AdminUserResponse row) => _adminRows.Add(row);

    /// <summary>
    /// Регистрирует профиль барбера, чтобы админская карточка показала его имя и статусы.
    /// </summary>
    public void AddBarberProfile(
        Guid id,
        string displayName,
        bool isVerified = true,
        bool isActive = true)
        => _barberProfiles[id] = new KnownBarberProfile(displayName, isVerified, isActive);

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

    /// <summary>
    /// Админская страница: те же фильтры, что у PostgreSQL-репозитория, но в памяти.
    /// </summary>
    /// <remarks>
    /// Повторяет порядок и семантику поиска (подстрока без учёта регистра, имя, фамилия
    /// или email), чтобы тесты обработчика проверяли решения, а не SQL.
    /// </remarks>
    public Task<Result<IReadOnlyList<AdminUserResponse>, Failure>> GetAdminPageAsync(
        AdminUserStatus status,
        UserRole? role,
        string? search,
        AdminUserCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        LastAdminStatus = status;
        LastAdminRole = role;
        LastAdminSearch = search;
        LastAdminLimit = limit;

        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminUserResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.AdminUser.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        // Строки задаются тестом явно: порядок проверяется по created_at, а не по времени
        // создания агрегата, которое в памяти совпадает до тика.
        IEnumerable<AdminUserResponse> query = _adminRows;

        query = status switch
        {
            AdminUserStatus.Active => query.Where(row => row.IsActive),
            AdminUserStatus.Blocked => query.Where(row => !row.IsActive),
            AdminUserStatus.Unverified => query.Where(row => !row.IsEmailVerified),
            _ => query
        };

        if (role is { } roleFilter)
        {
            query = query.Where(row => row.Role == roleFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();

            query = query.Where(row =>
                row.FirstName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.LastName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.Email.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (cursor is { } position)
        {
            query = query.Where(row =>
                row.CreatedAt < position.CreatedAt
                || (row.CreatedAt == position.CreatedAt
                    && row.Id.CompareTo(position.Id) > 0));
        }

        var rows = query
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<AdminUserResponse>, Failure>>(rows);
    }

    public Task<Result<AdminUserDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(id, out var user))
        {
            return Task.FromResult<Result<AdminUserDetailResponse, Failure>>(new Failure(
                Error.NotFound(
                    ApiErrorCodes.AdminUser.NotFound,
                    $"User '{id}' was not found.",
                    id)));
        }

        var profile = user.BarberProfileId is { } profileId
            && _barberProfiles.TryGetValue(profileId, out var knownProfile)
                ? new AdminUserBarberProfileResponse(
                    profileId,
                    knownProfile.DisplayName,
                    knownProfile.IsVerified,
                    knownProfile.IsActive)
                : user.BarberProfileId is { } linkedId
                    ? new AdminUserBarberProfileResponse(
                        linkedId,
                        "Профиль барбера",
                        IsVerified: false,
                        IsActive: false)
                    : null;

        var detail = new AdminUserDetailResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.Role,
            user.IsActive,
            user.IsEmailVerified,
            user.IsPhoneVerified,
            user.BarberProfileId,
            profile,
            user.LastLoginAt,
            user.LockoutEnd,
            user.FailedLoginAttempts,
            user.CreatedAt,
            user.UpdatedAt);

        return Task.FromResult<Result<AdminUserDetailResponse, Failure>>(detail);
    }

    public Task<Result<int, Failure>> CountActiveAdminsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<int, Failure>>(
            _users.Values.Count(user => user.Role == UserRole.Admin && user.IsActive));

    /// <summary>
    /// Блокировка администраторов в памяти не нужна: тесты выполняются последовательно,
    /// а проверяемое правило живёт в обработчике.
    /// </summary>
    public Task<UnitResult<Failure>> AcquireAdminLockAsync(
        CancellationToken cancellationToken = default)
    {
        AcquireAdminLockCallCount++;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

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

    /// <summary>
    /// Минимум данных профиля барбера, нужный админской карточке пользователя.
    /// </summary>
    private sealed record KnownBarberProfile(
        string DisplayName,
        bool IsVerified,
        bool IsActive);
}
