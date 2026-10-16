using BarberSearch.Application.Users;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="User"/> поверх <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context)
        : base(context)
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
            return NotFound("user.getById.notFound", nameof(User), id);
        }

        return user;
    }

    public async Task<Result<User, Failure>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new Failure(Error.Validation(
                "user.getByEmail.email",
                "Email cannot be empty.",
                nameof(email)));
        }

        // Сравнение точное: уникальный индекс ux_users_email построен по исходному значению,
        // поэтому приведение регистра сломало бы его использование.
        var trimmed = email.Trim();

        var user = await Context.Users
            .Include(x => x.FavoriteBarbers)
            .FirstOrDefaultAsync(x => x.Email == trimmed, cancellationToken);

        if (user is null)
        {
            return new Failure(Error.NotFound(
                "user.getByEmail.notFound",
                $"User with email '{trimmed}' was not found."));
        }

        return user;
    }

    public async Task<Result<IReadOnlyList<User>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(page, pageSize, "user.getPaged") is { } pagingError)
        {
            return pagingError;
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
                "user.emailExists.email",
                "Email cannot be empty.",
                nameof(email)));
        }

        var exists = await Context.Users
            .AsNoTracking()
            .AnyAsync(x => x.Email == email.Trim(), cancellationToken);

        return exists;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await Context.Users.AddAsync(user, cancellationToken);

        return await SaveChangesAsync("user.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        User user,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(user, "user.update", cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        Context.Users.Remove(user);

        return await SaveChangesAsync("user.delete", cancellationToken);
    }
}
