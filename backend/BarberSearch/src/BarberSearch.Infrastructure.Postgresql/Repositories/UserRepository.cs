using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
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
}
