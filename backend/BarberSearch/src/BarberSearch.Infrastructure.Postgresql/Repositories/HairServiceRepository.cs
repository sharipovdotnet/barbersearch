using BarberSearch.Application.Common;
using BarberSearch.Application.HairServices;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.Admin;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="HairService"/> — справочника услуг.
/// </summary>
/// <remarks>
/// Категория услуги живёт у специализации, поэтому все чтения справочника джойнят
/// <c>specializations</c>: фильтр и сортировка по категории, а также ответы API
/// получают её из одной строки, а не хранят копию у услуги.
/// </remarks>
public sealed class HairServiceRepository : RepositoryBase<HairService>, IHairServiceRepository
{
    public HairServiceRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query =
            from hairService in Context.HairServices.AsNoTracking()
            join specialization in Context.Specializations.AsNoTracking()
                on hairService.SpecializationId equals specialization.Id
            select new { HairService = hairService, Specialization = specialization };

        query = status switch
        {
            AdminCatalogueStatus.Active => query.Where(x => x.HairService.IsActive),
            AdminCatalogueStatus.Hidden => query.Where(x => !x.HairService.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);

            // Поиск ищет и по услуге, и по специализации: отдельного селекта-фильтра
            // по специализации в админском справочнике нет.
            query = query.Where(x =>
                EF.Functions.ILike(x.HairService.Name, pattern, "\\")
                || EF.Functions.ILike(x.Specialization.Name, pattern, "\\"));
        }

        // usageCount — не «число связей», а число активных подтверждённых барберов: неактивный
        // или неподтверждённый профиль, а также выключенная связь услуги не считаются,
        // иначе подтверждение «N барберов» соврёт.
        var rows = await query
            .OrderBy(x => x.Specialization.Category)
            .ThenBy(x => x.HairService.Name)
            .ThenBy(x => x.HairService.Id)
            .Select(x => new AdminHairServiceResponse(
                x.HairService.Id,
                x.HairService.Name,
                x.Specialization.Category,
                x.HairService.DefaultDurationMinutes,
                x.HairService.Description,
                x.HairService.IsActive,
                Context.BarberServices.Count(link =>
                    link.HairServiceId == x.HairService.Id
                    && link.IsActive
                    && Context.BarberProfiles.Any(profile =>
                        profile.Id == link.BarberProfileId
                        && profile.IsActive
                        && profile.IsVerified)),
                x.HairService.SpecializationId,
                x.Specialization.Name))
            .ToListAsync(cancellationToken);

        return rows;
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

    public async Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from hairService in Context.HairServices.AsNoTracking()
                join specialization in Context.Specializations.AsNoTracking()
                    on hairService.SpecializationId equals specialization.Id
                where hairService.IsActive
                orderby specialization.Category, hairService.Name
                select new HairServiceCatalogRow(hairService, specialization.Category))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.HairService.CategoryInvalid,
                DomainMessages.UnknownServiceCategory(category),
                nameof(category)));
        }

        var rows = await (
                from hairService in Context.HairServices.AsNoTracking()
                join specialization in Context.Specializations.AsNoTracking()
                    on hairService.SpecializationId equals specialization.Id
                where hairService.IsActive && specialization.Category == category
                orderby hairService.Name
                select new HairServiceCatalogRow(hairService, specialization.Category))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<HairServiceCatalogRow>, Failure>> GetPagedAsync(
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

        var query =
            from hairService in Context.HairServices.AsNoTracking()
            join specialization in Context.Specializations.AsNoTracking()
                on hairService.SpecializationId equals specialization.Id
            select new { HairService = hairService, Specialization = specialization };

        if (onlyActive)
        {
            query = query.Where(x => x.HairService.IsActive);
        }

        var rows = await query
            .OrderBy(x => x.Specialization.Category)
            .ThenBy(x => x.HairService.Name)
            .ThenBy(x => x.HairService.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new HairServiceCatalogRow(x.HairService, x.Specialization.Category))
            .ToListAsync(cancellationToken);

        return rows;
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
