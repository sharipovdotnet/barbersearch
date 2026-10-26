using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Salons;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Salon"/>.
/// </summary>
public sealed class SalonRepository : RepositoryBase<Salon>, ISalonRepository
{
    public SalonRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<Salon, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var salon = await Context.Salons
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (salon is null)
        {
            return NotFound(ApiErrorCodes.Salon.NotFound, nameof(Salon), id);
        }

        return salon;
    }

    public async Task<Result<IReadOnlyList<Salon>, Failure>> GetByCityAsync(
        string city,
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Salon.CityInvalid,
                "City cannot be empty.",
                nameof(city)));
        }

        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Salon.PagingPageInvalid,
                ApiErrorCodes.Salon.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        var query = Context.Salons
            .AsNoTracking()
            .Where(x => x.City == city.Trim());

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var salons = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return salons;
    }

    public async Task<Result<IReadOnlyList<Salon>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Salon.PagingPageInvalid,
                ApiErrorCodes.Salon.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        var query = Context.Salons.AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var salons = await query
            .OrderBy(x => x.City)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return salons;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.Salons
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(salon);

        await Context.Salons.AddAsync(salon, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.Salon.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(salon, ApiErrorCodes.Salon.UpdatePrefix, cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Salon salon,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(salon);

        Context.Salons.Remove(salon);

        return await SaveChangesAsync(ApiErrorCodes.Salon.DeletePrefix, cancellationToken);
    }
}
