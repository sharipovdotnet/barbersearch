using BarberSearch.Application.Common;
using BarberSearch.Application.Specializations;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Specializations.Admin;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Specializations;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Specialization"/> — справочника специализаций.
/// </summary>
public sealed class SpecializationRepository : RepositoryBase<Specialization>, ISpecializationRepository
{
    public SpecializationRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<IReadOnlyList<AdminSpecializationResponse>, Failure>> GetAdminListAsync(
        AdminCatalogueStatus status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Specializations.AsNoTracking();

        query = status switch
        {
            AdminCatalogueStatus.Active => query.Where(x => x.IsActive),
            AdminCatalogueStatus.Hidden => query.Where(x => !x.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);

            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }

        // Выключенная строка остаётся на карточках барберов, поэтому счётчик считает связи
        // с активными подтверждёнными профилями, а не «видимые» строки.
        var rows = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new AdminSpecializationResponse(
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                Context.BarberSpecializations.Count(link =>
                    link.SpecializationId == x.Id
                    && Context.BarberProfiles.Any(profile =>
                        profile.Id == link.BarberProfileId
                        && profile.IsActive
                        && profile.IsVerified))))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<Specialization, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var specialization = await Context.Specializations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (specialization is null)
        {
            return NotFound(ApiErrorCodes.Specialization.NotFound, nameof(Specialization), id);
        }

        return specialization;
    }

    public async Task<Result<IReadOnlyList<Specialization>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var specializations = await Context.Specializations
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return specializations;
    }

    public async Task<Result<Specialization, Failure>> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Specialization.NameInvalid,
                DomainMessages.SPECIALIZATION_NAME_REQUIRED,
                nameof(name)));
        }

        // Индекса по имени нет (справочник маленький), поэтому приведение регистра
        // не ухудшает план запроса.
        var normalized = name.Trim().ToLowerInvariant();

        var specialization = await Context.Specializations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalized, cancellationToken);

        if (specialization is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Specialization.NotFound,
                $"Specialization with name '{name.Trim()}' was not found."));
        }

        return specialization;
    }

    public async Task<Result<IReadOnlyList<Specialization>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Specialization.PagingPageInvalid,
                ApiErrorCodes.Specialization.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        var query = Context.Specializations.AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(x => x.IsActive);
        }

        var specializations = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return specializations;
    }

    public async Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await Context.Specializations
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);

        return exists;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        await Context.Specializations.AddAsync(specialization, cancellationToken);

        return await SaveChangesAsync(ApiErrorCodes.Specialization.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(
            specialization,
            ApiErrorCodes.Specialization.UpdatePrefix,
            cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        Context.Specializations.Remove(specialization);

        return await SaveChangesAsync(ApiErrorCodes.Specialization.DeletePrefix, cancellationToken);
    }
}
