using BarberSearch.Application.Common;
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
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="User"/> поверх <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class UserRepository : RepositoryBase<User>, IUserRepository
{
    /// <summary>
    /// Текст ошибки «email пуст».
    /// </summary>
    private const string EMAIL_REQUIRED = "Email cannot be empty.";

    public UserRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<User, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await Context.Users
            .Include(x => x.FavoriteBarbers)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound(ApiErrorCodes.User.NotFound, nameof(User), id);
        }

        return user;
    }

    /// <summary>
    /// Один узкий SELECT по первичному ключу: только <c>is_active</c> и <c>security_stamp</c>.
    /// </summary>
    /// <remarks>
    /// Ни <c>Include(FavoriteBarbers)</c>, ни материализации агрегата: проверке security stamp
    /// не нужен ни один столбец пользователя, кроме этих двух (R-11). Проекция в позиционный
    /// record транслируется EF Core в плоский <c>SELECT is_active, security_stamp … WHERE id = …</c>.
    /// </remarks>
    public async Task<Result<UserSecurityStamp, Failure>> GetSecurityStampAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var stamp = await Context.Users
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new UserSecurityStamp(x.IsActive, x.SecurityStamp))
            .SingleOrDefaultAsync(cancellationToken);

        if (stamp is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.User.SecurityStampNotFound,
                $"User with identifier '{id}' was not found.",
                id));
        }

        return stamp;
    }

    /// <summary>
    /// Один узкий SELECT по первичному ключу: <c>avatar_url</c>, <c>is_avatar_blocked</c>
    /// и <c>avatar_moderation_status</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ни <c>Include(FavoriteBarbers)</c>, ни материализации агрегата: раздаче нужны три
    /// столбца, чтобы сверить запрошенный media-URL с сохранённым и понять, скрыт ли файл
    /// администратором и одобрен ли он. Проекция в <see cref="UserAvatarAccess"/> транслируется
    /// EF Core в плоский <c>SELECT avatar_url, is_avatar_blocked, avatar_moderation_status
    /// … WHERE id = …</c>; позиционная запись (не анонимный тип) нужна, чтобы отличить
    /// «пользователя нет» (нет строки) от «аватара нет» (<c>NULL</c> в <c>avatar_url</c>).
    /// </para>
    /// <para>
    /// Отсутствие пользователя — <c>Error.NotFound</c> с кодом <c>avatarMedia.notFound</c>:
    /// раздача не раскрывает существование аккаунта отдельным кодом. Пользователь без
    /// аватара — успех с <c>null</c>.
    /// </para>
    /// </remarks>
    public async Task<Result<UserAvatarAccess, Failure>> GetAvatarAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var row = await BuildAvatarUrlQuery(Context, userId)
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return NotFound("avatarMedia.notFound", nameof(User), userId);
        }

        return row;
    }

    /// <summary>
    /// Строит узкий запрос доступа к аватару: три столбца по первичному ключу, без
    /// <c>Include</c> и без join'ов.
    /// </summary>
    /// <remarks>
    /// Вынесен в <c>internal static</c>, как и запрос пары «услуга барбера — услуга справочника»
    /// (R-20): тест проверяет трансляцию через <c>ToQueryString()</c>, не открывая соединение.
    /// Маски здесь нет: раздача решает по флагу блокировки и статусу проверки, отдавать ли
    /// файл администратору, владельцу или публично.
    /// </remarks>
    /// <param name="context">Контекст базы.</param>
    /// <param name="userId">Идентификатор пользователя.</param>
    internal static IQueryable<UserAvatarAccess> BuildAvatarUrlQuery(
        ApplicationDbContext context,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAvatarAccess(
                user.AvatarUrl,
                user.IsAvatarBlocked,
                user.AvatarModerationStatus));
    }

    public async Task<Result<User, Failure>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.User.EmailInvalid,
                EMAIL_REQUIRED,
                nameof(email)));
        }

        // Канонический адрес: вход принимает " User@Example.COM ", а в БД лежит
        // "user@example.com". Колонка citext делает сравнение регистронезависимым,
        // но нормализация сохраняет совпадение с уникальным индексом и текстом ошибки.
        var normalized = User.NormalizeEmail(email);

        var user = await Context.Users
            .Include(x => x.FavoriteBarbers)
            .FirstOrDefaultAsync(x => x.Email == normalized, cancellationToken);

        if (user is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.User.NotFound,
                $"User with email '{normalized}' was not found."));
        }

        return user;
    }

    /// <summary>
    /// Страница пользователей, свежие сверху. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать
    /// <see cref="MAX_PAGE_OFFSET"/> (10 000), иначе <c>400</c> с кодом
    /// <c>user.paging.depth</c> — до обращения к базе.
    /// </remarks>
    public async Task<Result<IReadOnlyList<User>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.User.PagingPageInvalid,
                ApiErrorCodes.User.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        if (ValidatePageOffset(
                page,
                pageSize,
                ApiErrorCodes.User.PagingDepthInvalid) is { } depthError)
        {
            return depthError;
        }

        var users = await Context.Users
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return users;
    }

    public async Task<Result<IReadOnlyList<AdminUserResponse>, Failure>> GetAdminPageAsync(
        AdminUserStatus status,
        UserRole? role,
        string? search,
        AdminUserCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminUser.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminUsersQuery(Context, status, role, search, cursor, limit)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Поиск активных пользователей по email или телефону для формы ручной записи.
    /// </summary>
    /// <remarks>
    /// Роль не фильтруется: барбер может записать клиента, администратора, коллегу
    /// и любого другого активного пользователя. Условие активности стоит до поиска.
    /// Шаблон строится тем же экранированием, что и в админском списке, поэтому «%»
    /// и «_» остаются литералами. Телефона может не быть — тогда строка совпадает
    /// только по email.
    /// </remarks>
    public async Task<Result<IReadOnlyList<BarberClientResponse>, Failure>> SearchClientsAsync(
        string search,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new List<BarberClientResponse>();
        }

        var pattern = BuildSearchPattern(search);

        var rows = await Context.Users
            .AsNoTracking()
            .Where(user => user.IsActive)
            .Where(user =>
                EF.Functions.ILike(user.Email, pattern, "\\")
                || (user.PhoneNumber != null
                    && EF.Functions.ILike(user.PhoneNumber, pattern, "\\")))
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
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<AdminUserDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await BuildAdminUserDetailQuery(Context, id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.AdminUser.NotFound,
                $"User '{id}' was not found.",
                id));
        }

        return ToAdminDetailResponse(row);
    }

    /// <summary>
    /// Страница очереди аватаров: только <c>Pending</c>-кадры, FIFO по времени поступления.
    /// </summary>
    /// <remarks>
    /// Предикат, порядок и курсор вынесены в <see cref="BuildAdminAvatarPhotosQuery"/>:
    /// тест проверяет трансляцию через <c>ToQueryString()</c> без базы, а счётчик сводки
    /// использует те же условия (<see cref="CountPendingAvatarsAsync"/>).
    /// </remarks>
    public async Task<Result<IReadOnlyList<AdminAvatarPhotoResponse>, Failure>>
        GetAdminAvatarPhotosPageAsync(
            AdminPhotoCursor? cursor,
            string? search,
            int limit,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminPhotos.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminAvatarPhotosQuery(Context, search, cursor, limit)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Строит очередь аватаров: предикат <c>Pending</c> + непустые URL и метка поступления,
    /// поиск по имени/фамилии/email, keyset-курсор и FIFO-порядок.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Прежний аватар при новой загрузке заменяется сразу, поэтому в очереди стоит ровно
    /// текущий кадр: <c>avatar_url IS NOT NULL</c> отсекает удалённый аватар, а
    /// <c>avatar_submitted_at IS NOT NULL</c> — строки, у которых метки поступления нет
    /// (так миграция описывает аккаунт без аватара). Условия обязаны совпадать со счётчиком.
    /// </para>
    /// <para>
    /// Поиск использует общий <see cref="LikePattern"/>: «%», «_» и «\» остаются литералами.
    /// Проекция отдаёт сырой URL и статус: администратор проверяет сам кадр, публичная маска
    /// здесь не применяется.
    /// </para>
    /// </remarks>
    /// <param name="context">Контекст базы.</param>
    /// <param name="search">Поиск по имени, фамилии или email; <c>null</c> — без фильтра.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    internal static IQueryable<AdminAvatarPhotoResponse> BuildAdminAvatarPhotosQuery(
        ApplicationDbContext context,
        string? search = null,
        AdminPhotoCursor? cursor = null,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var query = WherePendingAvatar(context.Users.AsNoTracking());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);

            query = query.Where(user =>
                EF.Functions.ILike(user.FirstName, pattern, "\\")
                || EF.Functions.ILike(user.LastName, pattern, "\\")
                || EF.Functions.ILike(user.Email, pattern, "\\"));
        }

        if (cursor is { } position)
        {
            // FIFO: продолжаем после последней пары (submitted_at, id). Сравнение по nullable
            // колонке безопасно: предикат выше уже отсекает NULL-метки.
            query = query.Where(user =>
                user.AvatarSubmittedAt > position.SubmittedAt
                || (user.AvatarSubmittedAt == position.SubmittedAt
                    && user.Id.CompareTo(position.Id) > 0));
        }

        var ordered = query
            .OrderBy(user => user.AvatarSubmittedAt)
            .ThenBy(user => user.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(user => new AdminAvatarPhotoResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.AvatarUrl!,
            user.AvatarModerationStatus,
            user.AvatarSubmittedAt!.Value));
    }

    /// <summary>
    /// Предикат очереди аватаров: <c>Pending</c> при непустых URL и метке поступления.
    /// </summary>
    /// <remarks>
    /// Один метод на чтение очереди и счётчик сводки: если условия разойдутся, число
    /// в сводке перестанет совпадать с размером вкладки «Аватары».
    /// </remarks>
    /// <param name="users">Пользователи без отслеживания.</param>
    internal static IQueryable<User> WherePendingAvatar(IQueryable<User> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return users.Where(user => user.AvatarModerationStatus == PhotoModerationStatus.Pending
                                   && user.AvatarUrl != null
                                   && user.AvatarSubmittedAt != null);
    }

    /// <summary>
    /// Сколько аватаров ждёт проверки — ровно предикат очереди.
    /// </summary>
    /// <remarks>
    /// Те же условия, что у <see cref="BuildAdminAvatarPhotosQuery"/>: статус <c>Pending</c>
    /// и непустые URL с меткой поступления. Добавление или снятие условия разошлось бы
    /// с размером вкладки «Аватары».
    /// </remarks>
    public async Task<Result<int, Failure>> CountPendingAvatarsAsync(
        CancellationToken cancellationToken = default)
    {
        var count = await WherePendingAvatar(Context.Users.AsNoTracking())
            .CountAsync(cancellationToken);

        return Result.Success<int, Failure>(count);
    }

    /// <summary>
    /// Сколько пользователей не подтвердило email — ровно предикат фильтра
    /// <c>AdminUserStatus.Unverified</c> при <c>role = all</c>.
    /// </summary>
    /// <remarks>
    /// Это контракт, а не деталь реализации: <c>is_email_verified = false</c> у всех ролей,
    /// включая заблокированных. Условие <c>is_active</c> или роль разошлись бы с размером
    /// вкладки «Без подтверждения» и со списком пользователей с тем же фильтром.
    /// </remarks>
    public async Task<Result<int, Failure>> CountUnverifiedAsync(
        CancellationToken cancellationToken = default)
    {
        var count = await Context.Users
            .AsNoTracking()
            .CountAsync(user => !user.IsEmailVerified, cancellationToken);

        return Result.Success<int, Failure>(count);
    }

    /// <summary>
    /// Узкая проекция профиля кабинета: без избранного, пароля и служебных полей.
    /// </summary>
    /// <remarks>
    /// Читается без отслеживания: экран только показывает данные. Отсутствие пользователя —
    /// <c>404 user.notFound</c>, как и у остальных чтений пользователя.
    /// </remarks>
    public async Task<Result<MyProfileResponse, Failure>> GetMyProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await Context.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new MyProfileResponse(
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
                user.AvatarRejectionReason))
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return NotFound(ApiErrorCodes.User.NotFound, nameof(User), id);
        }

        return profile;
    }

    public async Task<Result<int, Failure>> CountActiveAdminsAsync(
        CancellationToken cancellationToken = default)
        => await Context.Users
            .AsNoTracking()
            .CountAsync(
                user => user.Role == UserRole.Admin && user.IsActive,
                cancellationToken);

    public async Task<UnitResult<Failure>> AcquireAdminLockAsync(
        CancellationToken cancellationToken = default)
    {
        // Смена роли и блокировка администратора меняют состав небольшого множества,
        // которое нельзя оставить пустым. Транзакционная блокировка по постоянному ключу
        // сериализует эти операции: второй запрос увидит результат первого и не сможет
        // снять последнего администратора. Ключ — константа, а не идентификатор, потому
        // что проверка касается всего множества.
        try
        {
            // SET LOCAL действует до конца транзакции и только на это соединение.
            await Context.Database.ExecuteSqlRawAsync(
                $"SET LOCAL lock_timeout = '{PostgresSettings.LOCK_TIMEOUT}'",
                cancellationToken);

            await Context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended('barbersearch.admins', 0))",
                cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // 55P03: транзакцию откатит IUnitOfWork; клиенту отдаём 409, а не 500.
            return MapPostgresError(exception, ApiErrorCodes.User.UpdatePrefix);
        }
    }

    /// <summary>
    /// Админская выборка пользователей: фильтры статуса и роли, поиск и курсор.
    /// </summary>
    /// <remarks>
    /// Поиск идёт по имени, фамилии и email через <c>ILIKE</c>: регистр не важен, служебные
    /// символы шаблона экранируются, чтобы «%» в запросе не превращался в «найти всё».
    /// Список всегда читается свежими сверху, поэтому направление курсора одно.
    /// </remarks>
    internal static IQueryable<AdminUserResponse> BuildAdminUsersQuery(
        ApplicationDbContext context,
        AdminUserStatus status = AdminUserStatus.All,
        UserRole? role = null,
        string? search = null,
        AdminUserCursor? cursor = null,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var query = context.Users.AsNoTracking();

        query = status switch
        {
            AdminUserStatus.Active => query.Where(user => user.IsActive),
            AdminUserStatus.Blocked => query.Where(user => !user.IsActive),
            AdminUserStatus.Unverified => query.Where(user => !user.IsEmailVerified),
            _ => query
        };

        if (role is { } roleFilter)
        {
            query = query.Where(user => user.Role == roleFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = BuildSearchPattern(search);

            query = query.Where(user =>
                EF.Functions.ILike(user.FirstName, pattern, "\\")
                || EF.Functions.ILike(user.LastName, pattern, "\\")
                || EF.Functions.ILike(user.Email, pattern, "\\"));
        }

        if (cursor is { } position)
        {
            query = query.Where(user =>
                user.CreatedAt < position.CreatedAt
                || (user.CreatedAt == position.CreatedAt
                    && user.Id.CompareTo(position.Id) > 0));
        }

        var ordered = query
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(user => new AdminUserResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.IsEmailVerified,
            user.IsPhoneVerified,
            user.BarberProfileId,
            user.LastLoginAt,
            user.CreatedAt,
            user.AvatarUrl,
            user.IsAvatarBlocked,
            user.AvatarModerationStatus));
    }

    /// <summary>
    /// Админская карточка пользователя: один SQL с левым join по профилю барбера.
    /// </summary>
    /// <remarks>
    /// <c>LEFT JOIN</c> нужен, потому что профиль привязан не у всех: внутренний join
    /// выкинул бы клиентов и администраторов. Поля профиля остаются <c>null</c>, когда
    /// строки в <c>barber_profiles</c> нет, а DTO собирается из плоской строки уже в памяти.
    /// </remarks>
    internal static IQueryable<AdminUserDetailRow> BuildAdminUserDetailQuery(
        ApplicationDbContext context,
        Guid id)
    {
        ArgumentNullException.ThrowIfNull(context);

        return from user in context.Users.AsNoTracking()
               join barberProfile in context.BarberProfiles.AsNoTracking()
                   on user.BarberProfileId equals (Guid?)barberProfile.Id into profiles
               from barberProfile in profiles.DefaultIfEmpty()
               where user.Id == id
               select new AdminUserDetailRow(
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
                   barberProfile == null ? null : barberProfile.DisplayName.Value,
                   barberProfile == null ? null : (bool?)barberProfile.IsVerified,
                   barberProfile == null ? null : (bool?)barberProfile.IsActive,
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
    }

    private static AdminUserDetailResponse ToAdminDetailResponse(AdminUserDetailRow row)
        => new(
            row.Id,
            row.Email,
            row.FirstName,
            row.LastName,
            row.PhoneNumber,
            row.AvatarUrl,
            row.Role,
            row.IsActive,
            row.IsEmailVerified,
            row.IsPhoneVerified,
            row.BarberProfileId,
            row.BarberProfileId is { } profileId
                ? new AdminUserBarberProfileResponse(
                    profileId,
                    row.BarberDisplayName ?? string.Empty,
                    row.BarberIsVerified ?? false,
                    row.BarberIsActive ?? false)
                : null,
            row.LastLoginAt,
            row.LockoutEnd,
            row.FailedLoginAttempts,
            row.CreatedAt,
            row.UpdatedAt,
            row.ConsentPolicyVersion,
            row.ConsentedAt,
            row.IsAvatarBlocked,
            row.AvatarModerationStatus,
            row.AvatarRejectionReason);

    /// <summary>
    /// Шаблон <c>ILIKE</c> для поиска по имени, фамилии и email.
    /// </summary>
    /// <remarks>
    /// <c>%</c>, <c>_</c> и <c>\</c> из пользовательского запроса экранируются: иначе поиск
    /// «100%» находил бы всё, а «_» работал бы как любой одиночный символ.
    /// </remarks>
    private static string BuildSearchPattern(string search)
        => "%" + search
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
        + "%";

    public async Task<Result<bool, Failure>> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.User.EmailInvalid,
                EMAIL_REQUIRED,
                nameof(email)));
        }

        var normalized = User.NormalizeEmail(email);

        var exists = await Context.Users
            .AsNoTracking()
            .AnyAsync(x => x.Email == normalized, cancellationToken);

        return exists;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await Context.Users.AddAsync(user, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.User.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        User user,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(user, ApiErrorCodes.User.UpdatePrefix, cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        Context.Users.Remove(user);

        return await SaveChangesAsync(ApiErrorCodes.User.DeletePrefix, cancellationToken);
    }

    /// <summary>
    /// Плоская строка админской карточки: пользователь и необязательный профиль барбера.
    /// </summary>
    /// <remarks>
    /// Отдельная запись, а не DTO: <c>LEFT JOIN</c> оставляет поля профиля пустыми, и
    /// превращение в <see cref="AdminUserBarberProfileResponse"/> делается в памяти, чтобы
    /// EF Core не собирал вложенный объект с nullable-полями.
    /// </remarks>
    internal sealed record AdminUserDetailRow(
        Guid Id,
        string Email,
        string FirstName,
        string LastName,
        string? PhoneNumber,
        string? AvatarUrl,
        UserRole Role,
        bool IsActive,
        bool IsEmailVerified,
        bool IsPhoneVerified,
        Guid? BarberProfileId,
        string? BarberDisplayName,
        bool? BarberIsVerified,
        bool? BarberIsActive,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset? LockoutEnd,
        int FailedLoginAttempts,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? ConsentPolicyVersion,
        DateTimeOffset? ConsentedAt,
        bool IsAvatarBlocked,
        PhotoModerationStatus AvatarModerationStatus,
        string? AvatarRejectionReason);
}
