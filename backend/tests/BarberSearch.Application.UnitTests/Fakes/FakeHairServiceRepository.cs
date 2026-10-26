using BarberSearch.Application.HairServices;
using BarberSearch.Domain.HairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IHairServiceRepository"/> для тестов проверок ссылочной целостности.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>.
/// </remarks>
public sealed class FakeHairServiceRepository : IHairServiceRepository
{
    private readonly HashSet<Guid> _existingIds = [];

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<HairService, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetByCategoryAsync(
        ServiceCategory category,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<HairService>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> UpdateAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> DeleteAsync(
        HairService hairService,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
