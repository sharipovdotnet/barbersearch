using BarberSearch.Application.BarberProfiles;
using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="BarberProfile"/>.
/// </summary>
public sealed class BarberProfileRepository : RepositoryBase<BarberProfile>, IBarberProfileRepository
{
    public BarberProfileRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Result<BarberProfile, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await FullAggregate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (profile is null)
        {
            return NotFound("barberProfile.getById.notFound", nameof(BarberProfile), id);
        }

        return profile;
    }

    public async Task<Result<BarberProfile, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await FullAggregate()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return new Failure(Error.NotFound(
                "barberProfile.getByUserId.notFound",
                $"Barber profile of user '{userId}' was not found.",
                userId));
        }

        return profile;
    }

    public async Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.Distinct().ToArray();

        if (idList.Length == 0)
        {
            return Array.Empty<BarberProfile>();
        }

        // Только корни, без дочерних коллекций: это путь поиска и расчёта слотов,
        // где Include пяти коллекций дал бы декартов взрыв (AGENT.md §15.6).
        var profiles = await Context.BarberProfiles
            .AsNoTracking()
            .Where(x => idList.Contains(x.Id))
            .ToListAsync(cancellationToken);

        return profiles;
    }

    public async Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(page, pageSize, "barberProfile.getPaged") is { } pagingError)
        {
            return pagingError;
        }

        var query = Context.BarberProfiles.AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var profiles = await query
            // В PostgreSQL ORDER BY ... DESC ставит NULL первыми, поэтому «профили без отзывов»
            // отодвигаются явным ключом (rating IS NULL): сначала с рейтингом, потом остальные.
            .OrderBy(x => x.Rating == null)
            .ThenByDescending(x => x.Rating)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return profiles;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.BarberProfiles
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<Result<bool, Failure>> IsOwnedByAsync(
        Guid barberProfileId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var isOwned = await Context.BarberProfiles
            .AsNoTracking()
            .AnyAsync(x => x.Id == barberProfileId && x.UserId == userId, cancellationToken);

        return isOwned;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(barberProfile);

        await Context.BarberProfiles.AddAsync(barberProfile, cancellationToken);

        return await SaveChangesAsync("barberProfile.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(barberProfile, "barberProfile.update", cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(barberProfile);

        Context.BarberProfiles.Remove(barberProfile);

        return await SaveChangesAsync("barberProfile.delete", cancellationToken);
    }

    /// <summary>
    /// Запрос профиля со всеми дочерними коллекциями.
    /// </summary>
    /// <remarks>
    /// <c>AsSplitQuery</c> обязателен: пять коллекций в одном JOIN дают декартово произведение
    /// строк, то есть и лишний трафик, и неверный результат при агрегатах.
    /// </remarks>
    private IQueryable<BarberProfile> FullAggregate()
        => Context.BarberProfiles
            .Include(x => x.Salons)
                .ThenInclude(salon => salon.WorkingSchedules)
            .Include(x => x.Services)
            .Include(x => x.PortfolioPhotos)
            .Include(x => x.BarberSpecializations)
            .Include(x => x.TimeOffs)
            .AsSplitQuery();
}
