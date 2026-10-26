using BarberSearch.Application.HairServices;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="HairService"/> — справочника услуг.
/// </summary>
public sealed class HairServiceRepository : RepositoryBase<HairService>, IHairServiceRepository
{
    public HairServiceRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<HairService, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var hairService = await Context.HairServices
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (hairService is null)
        {
            return NotFound(ApiErrorCodes.HairService.NotFound, nameof(HairService), id);
        }

        return hairService;
    }

    public async Task<Result<IReadOnlyList<HairService>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var hairServices = await Context.HairServices
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return hairServices;
    }

    public async Task<Result<IReadOnlyList<HairService>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.HairService.CategoryInvalid,
                $"Unknown service category value {(int)category}.",
                nameof(category)));
        }

        var hairServices = await Context.HairServices
            .AsNoTracking()
            .Where(x => x.IsActive && x.Category == category)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return hairServices;
    }

    public async Task<Result<IReadOnlyList<HairService>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.HairService.PagingPageInvalid,
                ApiErrorCodes.HairService.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        var query = Context.HairServices.AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var hairServices = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return hairServices;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.HairServices
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hairService);

        await Context.HairServices.AddAsync(hairService, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.HairService.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(hairService, ApiErrorCodes.HairService.UpdatePrefix, cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hairService);

        Context.HairServices.Remove(hairService);

        return await SaveChangesAsync(ApiErrorCodes.HairService.DeletePrefix, cancellationToken);
    }
}
