using BarberSearch.Application.Specializations;
using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="ISpecializationRepository"/> для тестов проверок ссылочной целостности
/// и чтения справочника.
/// </summary>
/// <remarks>
/// Реализовано только то, что вызывают проверяемые сценарии: <see cref="ExistsAsync"/>
/// и <see cref="GetActiveAsync"/>.
/// </remarks>
public sealed class FakeSpecializationRepository : ISpecializationRepository
{
    private readonly HashSet<Guid> _existingIds = [];
    private readonly List<Specialization> _active = [];

    /// <summary>Сбой чтения справочника: обработчик обязан пробросить его как <c>Result</c>.</summary>
    public Failure? GetActiveFailure { get; set; }

    public void AddExisting(Guid id) => _existingIds.Add(id);

    public void AddActive(Specialization specialization) => _active.Add(specialization);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_existingIds.Contains(id));

    public Task<Result<IReadOnlyList<Specialization>, Failure>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        if (GetActiveFailure is { } failure)
        {
            return Task.FromResult<Result<IReadOnlyList<Specialization>, Failure>>(failure);
        }

        return Task.FromResult<Result<IReadOnlyList<Specialization>, Failure>>(_active);
    }

    public Task<Result<Specialization, Failure>> GetByIdAsync(
        Guid id,
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
