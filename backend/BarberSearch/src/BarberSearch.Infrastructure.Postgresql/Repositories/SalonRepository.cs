using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
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

    public async Task<Result<IReadOnlyList<AdminSalonResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Salons.AsNoTracking();

        // «Видимые» и «выключенные» считаются только среди одобренных салонов: pending
        // не должен ни попадать в активный каталог, ни раздувать счётчик выключенных.
        query = status switch
        {
            AdminCatalogueStatus.Active => query.Where(x =>
                x.IsActive && x.ModerationStatus == SalonModerationStatus.Active),
            AdminCatalogueStatus.Hidden => query.Where(x =>
                !x.IsActive && x.ModerationStatus == SalonModerationStatus.Active),
            AdminCatalogueStatus.Pending => query.Where(x =>
                x.ModerationStatus == SalonModerationStatus.Pending),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);

            // Салоны ищут и по городу: администратор знает салон по городу чаще, чем по имени.
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern, "\\")
                || EF.Functions.ILike(x.City, pattern, "\\"));
        }

        // Связи с салоном не удаляются при выключении, поэтому счётчик показывает, сколько
        // активных подтверждённых барберов останутся привязанными к выключенной строке.
        var rows = await (
            from salon in query
            join user in Context.Users.AsNoTracking() on salon.OwnerUserId equals (Guid?)user.Id into owners
            from owner in owners.DefaultIfEmpty()
            orderby salon.City, salon.Name, salon.Id
            select new AdminSalonResponse(
                salon.Id,
                salon.Name,
                salon.Address,
                salon.City,
                salon.PostalCode,
                salon.Country,
                salon.TimeZoneId,
                salon.Latitude,
                salon.Longitude,
                salon.PhoneNumber,
                salon.WebsiteUrl,
                salon.IsActive,
                salon.ModerationStatus,
                salon.RejectionReason,
                salon.ReviewedAt,
                owner == null ? null : owner.Email,
                Context.BarberSalons.Count(link =>
                    link.SalonId == salon.Id
                    && Context.BarberProfiles.Any(profile =>
                        profile.Id == link.BarberProfileId
                        && profile.IsActive
                        && profile.IsVerified))))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<SalonDirectoryItemResponse>, Failure>> GetDirectoryAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Salons
            .AsNoTracking()
            .Where(salon =>
                salon.IsActive && salon.ModerationStatus == SalonModerationStatus.Active);

        if (city is not null)
        {
            query = query.Where(salon => salon.City == city);
        }

        if (search is not null)
        {
            var pattern = LikePattern.Contains(search);

            query = query.Where(salon =>
                EF.Functions.ILike(salon.Name, pattern, "\\")
                || EF.Functions.ILike(salon.Address, pattern, "\\"));
        }

        var rows = await query
            .OrderBy(salon => salon.City)
            .ThenBy(salon => salon.Name)
            .ThenBy(salon => salon.Id)
            .Select(salon => new SalonDirectoryItemResponse(
                salon.Id,
                salon.Name,
                salon.City,
                salon.Address,
                salon.TimeZoneId))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Сколько салонов выключено — ровно <see cref="AdminCatalogueStatus.Hidden"/>.
    /// </summary>
    /// <remarks>
    /// Предикат <c>is_active = false AND moderation_status = 2</c> — тот же, что у
    /// <see cref="GetAdminListAsync"/> со статусом <c>Hidden</c>: число в сводке обязано
    /// совпадать с разделом каталога. Салон на проверке тоже неактивен, но он не «выключен
    /// администратором» и в счётчик не входит.
    /// Связи барберов с выключенным салоном сохраняются, поэтому join'ов у счётчика нет.
    /// </remarks>
    public async Task<Result<int, Failure>> CountInactiveAsync(
        CancellationToken cancellationToken = default)
    {
        var count = await Context.Salons
            .AsNoTracking()
            .CountAsync(
                salon => !salon.IsActive
                         && salon.ModerationStatus == SalonModerationStatus.Active,
                cancellationToken);

        return Result.Success<int, Failure>(count);
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

    public async Task<Result<IReadOnlyList<Salon>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.Distinct().ToArray();

        if (idList.Length == 0)
        {
            return Result.Success<IReadOnlyList<Salon>, Failure>([]);
        }

        var salons = await Context.Salons
            .AsNoTracking()
            .Where(salon => idList.Contains(salon.Id))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<Salon>, Failure>(salons);
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
            query = query.Where(x =>
                x.IsActive && x.ModerationStatus == SalonModerationStatus.Active);
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
            query = query.Where(x =>
                x.IsActive && x.ModerationStatus == SalonModerationStatus.Active);
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
