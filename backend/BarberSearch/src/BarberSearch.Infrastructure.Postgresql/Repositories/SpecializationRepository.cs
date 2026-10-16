using BarberSearch.Application.Specializations;
using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Specialization"/> — справочника специализаций.
/// </summary>
public sealed class SpecializationRepository : RepositoryBase<Specialization>, ISpecializationRepository
{
    public SpecializationRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Result<Specialization, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var specialization = await Context.Specializations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (specialization is null)
        {
            return NotFound("specialization.getById.notFound", nameof(Specialization), id);
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
                "specialization.getByName.name",
                "Specialization name cannot be empty.",
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
                "specialization.getByName.notFound",
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
        if (ValidatePaging(page, pageSize, "specialization.getPaged") is { } pagingError)
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

    public async Task<UnitResult<Failure>> AddAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        await Context.Specializations.AddAsync(specialization, cancellationToken);

        return await SaveChangesAsync("specialization.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(specialization, "specialization.update", cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        Context.Specializations.Remove(specialization);

        return await SaveChangesAsync("specialization.delete", cancellationToken);
    }
}
