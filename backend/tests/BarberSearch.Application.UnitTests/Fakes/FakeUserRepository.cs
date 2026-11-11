using System.Reflection;
using BarberSearch.Application.Photos.Admin;
using BarberSearch.Application.Users;
using BarberSearch.Application.Users.Admin;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
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
    private readonly List<AdminAvatarPhotoResponse> _adminAvatarRows = [];
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
    /// Поиск, курсор и лимит последнего запроса очереди аватаров: тесты проверяют, что
    /// обработчик передал обрезанную строку, разобранный курсор и pageSize + 1.
    /// </summary>
    public string? LastAvatarPhotoSearch { get; private set; }

    public AdminPhotoCursor? LastAvatarPhotoCursor { get; private set; }

    public int? LastAvatarPhotoLimit { get; private set; }

    /// <summary>Ответ <see cref="CountPendingAvatarsAsync"/>: сколько аватаров ждёт проверки.</summary>
    public int PendingAvatarsCount { get; set; }

    /// <summary>Подмена ошибки счётчика очереди аватаров.</summary>
    public Failure? CountPendingAvatarsFailure { get; set; }

    /// <summary>Сколько раз сводка читала счётчик аватаров.</summary>
    public int CountPendingAvatarsCallCount { get; private set; }

    /// <summary>
    /// Хук перед счётчиком аватаров: тест сводки проверяет, что чтения идут
    /// последовательно — счётчик портфолио не стартует раньше этого вызова.
    /// </summary>
    public Action? BeforeCountPendingAvatars { get; set; }

    /// <summary>
    /// Сколько раз обработчик брал блокировку администраторов.
    /// </summary>
    public int AcquireAdminLockCallCount { get; private set; }

    /// <summary>
    /// Сколько раз обработчики загружали агрегат через <see cref="GetByIdAsync"/>.
    /// </summary>
    public int GetByIdCallCount { get; private set; }

    /// <summary>
    /// Сколько раз обработчики искали пользователя по email.
    /// </summary>
    public int GetByEmailCallCount { get; private set; }

    /// <summary>
    /// Адрес последнего поиска по email: тест проверяет, что обработчик передал
    /// канонический вид, а не исходную строку.
    /// </summary>
    public string? LastGetByEmail { get; private set; }

    /// <summary>
    /// Подмена чтения по идентификатору: тест проверяет, что не-«not found» ошибка
    /// репозитория не превращается в <c>400</c>.
    /// </summary>
    public Func<Guid, Result<User, Failure>>? GetByIdBehavior { get; set; }

    /// <summary>
    /// Подмена поиска по email: тест проверяет неожиданную ошибку репозитория и
    /// отдельный код <c>user.email.invalid</c>.
    /// </summary>
    public Func<string, Result<User, Failure>>? GetByEmailBehavior { get; set; }

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
    /// Добавляет строку очереди аватаров: обработчик проверяет разбор параметров и курсор,
    /// а фильтрацию Pending выполняет настоящий SQL, поэтому фейк хранит уже готовые строки.
    /// </summary>
    public void AddAdminAvatarRow(AdminAvatarPhotoResponse row) => _adminAvatarRows.Add(row);

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

        if (GetByIdBehavior is { } behavior)
        {
            return Task.FromResult(behavior(id));
        }

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

    /// <summary>
    /// Доступ к аватару из «таблицы» пользователей; отсутствие пользователя — тот же
    /// <c>404 avatarMedia.notFound</c>, что и в PostgreSQL-репозитории.
    /// </summary>
    public Task<Result<UserAvatarAccess, Failure>> GetAvatarAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            return Task.FromResult<Result<UserAvatarAccess, Failure>>(new Failure(
                Error.NotFound(
                    "avatarMedia.notFound",
                    $"User '{userId}' was not found.",
                    userId)));
        }

        return Task.FromResult<Result<UserAvatarAccess, Failure>>(
            new UserAvatarAccess(
                user.AvatarUrl,
                user.IsAvatarBlocked,
                user.AvatarModerationStatus));
    }

    public Task<Result<User, Failure>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        GetByEmailCallCount++;
        LastGetByEmail = User.NormalizeEmail(email);

        if (GetByEmailBehavior is { } behavior)
        {
            return Task.FromResult(behavior(email));
        }

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
            user.UpdatedAt,
            user.ConsentPolicyVersion,
            user.ConsentedAt,
            user.IsAvatarBlocked,
            user.AvatarModerationStatus,
            user.AvatarRejectionReason);

        return Task.FromResult<Result<AdminUserDetailResponse, Failure>>(detail);
    }

    /// <summary>
    /// Очередь аватаров в памяти: те же FIFO-порядок, курсор и поиск, что у PostgreSQL,
    /// но готовые строки задаёт тест — статус <c>Pending</c> фильтрует настоящий SQL.
    /// </summary>
    public Task<Result<IReadOnlyList<AdminAvatarPhotoResponse>, Failure>>
        GetAdminAvatarPhotosPageAsync(
            AdminPhotoCursor? cursor,
            string? search,
            int limit,
            CancellationToken cancellationToken = default)
    {
        LastAvatarPhotoSearch = search;
        LastAvatarPhotoCursor = cursor;
        LastAvatarPhotoLimit = limit;

        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminAvatarPhotoResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.AdminPhotos.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        IEnumerable<AdminAvatarPhotoResponse> query = _adminAvatarRows;

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
                row.SubmittedAt > position.SubmittedAt
                || (row.SubmittedAt == position.SubmittedAt
                    && row.UserId.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<AdminAvatarPhotoResponse> rows = query
            .OrderBy(row => row.SubmittedAt)
            .ThenBy(row => row.UserId)
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminAvatarPhotoResponse>, Failure>(rows));
    }

    /// <summary>
    /// Счётчик аватаров на проверке; тест задаёт значение и ошибку явно.
    /// </summary>
    public Task<Result<int, Failure>> CountPendingAvatarsAsync(
        CancellationToken cancellationToken = default)
    {
        CountPendingAvatarsCallCount++;
        BeforeCountPendingAvatars?.Invoke();

        return Task.FromResult(CountPendingAvatarsFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(PendingAvatarsCount));
    }

    /// <summary>
    /// Та же узкая проекция, что в PostgreSQL-репозитории: без избранного и пароля.
    /// </summary>
    public Task<Result<MyProfileResponse, Failure>> GetMyProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(id, out var user))
        {
            return Task.FromResult<Result<MyProfileResponse, Failure>>(new Failure(
                Error.NotFound(
                    ApiErrorCodes.User.NotFound,
                    $"User '{id}' was not found.",
                    id)));
        }

        return Task.FromResult<Result<MyProfileResponse, Failure>>(
            new MyProfileResponse(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.Role,
                user.BarberProfileId,
                user.IsEmailVerified,
                user.IsPhoneVerified,
                user.CreatedAt,
                user.LastLoginAt,
                user.IsAvatarBlocked,
                user.AvatarModerationStatus,
                user.AvatarRejectionReason));
    }

    public Task<Result<int, Failure>> CountActiveAdminsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<int, Failure>>(
            _users.Values.Count(user => user.Role == UserRole.Admin && user.IsActive));

    /// <summary>
    /// Поиск пользователей в памяти с той же семантикой, что у PostgreSQL-репозитория:
    /// только активные пользователи любой роли, подстрока без учёта регистра по email
    /// и телефону, порядок по имени и ограничение выдачи.
    /// </summary>
    public Task<Result<IReadOnlyList<BarberClientResponse>, Failure>> SearchClientsAsync(
        string search,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var needle = search.Trim();

        var rows = _users.Values
            .Where(user => user.IsActive)
            .Where(user =>
                user.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (user.PhoneNumber?.Contains(
                        needle,
                        StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ThenBy(user => user.Id)
            .Take(limit)
            .Select(user => new BarberClientResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.PhoneNumber))
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<BarberClientResponse>, Failure>>(rows);
    }

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
