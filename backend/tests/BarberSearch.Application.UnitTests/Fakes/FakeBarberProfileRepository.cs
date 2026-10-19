using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberProfileRepository"/> для тестов обработчиков: реализованы
/// только те члены, которые вызывают проверяемые сценарии.
/// </summary>
/// <remarks>
/// Поиск в фейке повторяет keyset-порядок настоящего репозитория
/// (<c>rating DESC NULLS LAST, id</c>), поэтому на нём можно проверять стык страниц.
/// Фильтры по городу, специализации, услуге и дате фейк не применяет: тесты проверяют,
/// что обработчик передал их в репозиторий без искажений.
/// </remarks>
public sealed class FakeBarberProfileRepository : IBarberProfileRepository
{
    private readonly Dictionary<Guid, BarberProfile> _profiles = [];
    private readonly List<BarberSearchItemResponse> _searchItems = [];
    private readonly Dictionary<Guid, BarberProfileResponse> _cards = [];

    public IReadOnlyCollection<BarberProfile> Profiles => _profiles.Values;

    public IReadOnlyCollection<BarberSearchItemResponse> SearchItems => _searchItems;

    public BarberSearchFilter? LastSearchFilter { get; private set; }

    public void Add(BarberProfile profile) => _profiles[profile.Id] = profile;

    public void AddSearchItem(BarberSearchItemResponse item) => _searchItems.Add(item);

    public void AddCard(BarberProfileResponse card) => _cards[card.Id] = card;

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

    public Task<Result<IReadOnlyList<BarberSearchItemResponse>, Failure>> SearchAsync(
        BarberSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        LastSearchFilter = filter;

        var ordered = _searchItems
            .OrderBy(item => item.Rating == null)
            .ThenByDescending(item => item.Rating)
            .ThenBy(item => item.Id)
            .AsEnumerable();

        if (filter.Cursor is { } cursor)
        {
            ordered = cursor.Rating is null
                ? ordered.Where(item =>
                    item.Rating == null
                    && item.Id.CompareTo(cursor.Id) > 0)
                : ordered.Where(item =>
                    item.Rating == null
                    || item.Rating < cursor.Rating
                    || (item.Rating == cursor.Rating && item.Id.CompareTo(cursor.Id) > 0));
        }

        IReadOnlyList<BarberSearchItemResponse> result = ordered
            .Take(filter.Limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<BarberSearchItemResponse>, Failure>(result));
    }

    public Task<Result<BarberProfileResponse, Failure>> GetCardByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (_cards.TryGetValue(id, out var card))
        {
            return Task.FromResult<Result<BarberProfileResponse, Failure>>(card);
        }

        return Task.FromResult<Result<BarberProfileResponse, Failure>>(new Failure(Error.NotFound(
            "barberProfile.getCardById.notFound",
            $"Barber profile '{id}' was not found.",
            id)));
    }

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
