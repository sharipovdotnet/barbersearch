using BarberSearch.Application.BarberProfiles;
using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberProfileRepository"/> для тестов обработчиков: реализованы
/// только те члены, которые вызывают проверяемые сценарии.
/// </summary>
public sealed class FakeBarberProfileRepository : IBarberProfileRepository
{
    private readonly Dictionary<Guid, BarberProfile> _profiles = [];

    public IReadOnlyCollection<BarberProfile> Profiles => _profiles.Values;

    public void Add(BarberProfile profile) => _profiles[profile.Id] = profile;

    public Task<Result<BarberProfile, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<BarberProfile, Failure>>(
            _profiles.TryGetValue(id, out var profile)
                ? profile
                : new Failure(Error.NotFound(
                    "barberProfile.getById.notFound",
                    $"Barber profile '{id}' was not found.",
                    id)));

    public Task<Result<BarberProfile, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = _profiles.Values.FirstOrDefault(x => x.UserId == userId);

        if (profile is null)
        {
            return Task.FromResult<Result<BarberProfile, Failure>>(new Failure(Error.NotFound(
                "barberProfile.getByUserId.notFound",
                $"Barber profile of user '{userId}' was not found.",
                userId)));
        }

        return Task.FromResult<Result<BarberProfile, Failure>>(profile);
    }

    public Task<Result<bool, Failure>> IsOwnedByAsync(
        Guid barberProfileId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(
            _profiles.TryGetValue(barberProfileId, out var profile) && profile.UserId == userId);

    public Task<Result<bool, Failure>> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(_profiles.ContainsKey(id));

    public Task<UnitResult<Failure>> AddAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        _profiles[barberProfile.Id] = barberProfile;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UnitResult.Success<Failure>());

    public Task<UnitResult<Failure>> DeleteAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<BarberProfile>, Failure>> GetPagedAsync(
        int page,
        int pageSize,
        bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
