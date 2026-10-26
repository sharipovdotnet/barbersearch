using BarberSearch.Application.Specializations;
using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="ISpecializationRepository"/> для тестов проверок ссылочной целостности.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>.
/// </remarks>
public sealed class FakeSpecializationRepository : ISpecializationRepository
{
    private readonly HashSet<Guid> _existingIds = [];

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<Specialization, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<Specialization>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<Specialization, Failure>> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<Specialization>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> UpdateAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> DeleteAsync(
        Specialization specialization,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
