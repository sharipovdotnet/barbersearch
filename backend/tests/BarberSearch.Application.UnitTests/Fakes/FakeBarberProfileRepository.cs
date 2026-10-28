using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using CSharpFunctionalExtensions;
using Shared;
using System.Reflection;

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
public sealed class FakeBarberProfileRepository : IBarberProfileRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, BarberProfile> _profiles = [];
    private readonly List<BarberSearchItemResponse> _searchItems = [];
    private readonly Dictionary<Guid, BarberProfileResponse> _cards = [];
    private readonly List<AdminBarberResponse> _adminBarbers = [];
    private readonly Dictionary<Guid, AdminBarberDetailResponse> _adminDetails = [];
    private Dictionary<Guid, BarberProfile>? _snapshot;

    public IReadOnlyCollection<BarberProfile> Profiles => _profiles.Values;

    public IReadOnlyCollection<BarberSearchItemResponse> SearchItems => _searchItems;

    public BarberSearchFilter? LastSearchFilter { get; private set; }

    /// <summary>
    /// Что обработчик передал в админский список: статус, поиск, курсор и лимит.
    /// </summary>
    public AdminBarberStatus? LastAdminStatus { get; private set; }

    public string? LastAdminSearch { get; private set; }

    public AdminBarberCursor? LastAdminCursor { get; private set; }

    public int? LastAdminLimit { get; private set; }

    /// <summary>
    /// Какие узкие чтения профиля вызвал обработчик: <c>root</c>, <c>salons</c>,
    /// <c>salons+schedules</c>, <c>services</c>, <c>photos</c>, <c>specializations</c>,
    /// <c>timeOffs</c>, <c>owner</c>, <c>salon</c>. Тесты R-12 проверяют, что обработчик
    /// не грузит лишние коллекции.
    /// </summary>
    public List<string> LoadCalls { get; } = [];

    public BarberSlotContext? SlotContext { get; set; }

    /// <summary>
    /// Подмена сохранения профиля: тест проверяет откат транзакции при ошибке записи.
    /// </summary>
    public Func<BarberProfile, UnitResult<Failure>>? UpdateBehavior { get; set; }

    /// <summary>
    /// Подмена вставки профиля: тест проверяет маппинг гонки unique violation (R-28).
    /// </summary>
    public Func<BarberProfile, UnitResult<Failure>>? AddBehavior { get; set; }

    public void Add(BarberProfile profile) => _profiles[profile.Id] = profile;

    public void BeginTransaction()
        => _snapshot = _profiles.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _profiles.Clear();

        foreach (var entry in _snapshot)
        {
            _profiles[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    public void AddSearchItem(BarberSearchItemResponse item) => _searchItems.Add(item);

    public void AddCard(BarberProfileResponse card) => _cards[card.Id] = card;

    public void AddAdminBarber(AdminBarberResponse barber) => _adminBarbers.Add(barber);

    public void AddAdminBarberDetail(AdminBarberDetailResponse barber)
        => _adminDetails[barber.Id] = barber;

    /// <summary>
    /// Профиль из памяти — для проверок состояния после выполнения обработчика.
    /// В интерфейс репозитория не входит: обработчики обязаны пользоваться узкими чтениями.
    /// </summary>
    public BarberProfile? Find(Guid id)
        => _profiles.GetValueOrDefault(id);

    public Task<Result<BarberProfile, Failure>> GetRootByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("root", id);

    public Task<Result<BarberProfile, Failure>> GetWithSalonsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("salons", id);

    public Task<Result<BarberProfile, Failure>> GetWithSalonsAndSchedulesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("salons+schedules", id);

    public Task<Result<BarberProfile, Failure>> GetWithServicesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("services", id);

    public Task<Result<BarberProfile, Failure>> GetWithPhotosAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("photos", id);

    public Task<Result<BarberProfile, Failure>> GetWithSpecializationsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("specializations", id);

    public Task<Result<BarberProfile, Failure>> GetWithTimeOffsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("timeOffs", id);

    public Task<Result<Guid, Failure>> GetOwnerIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        LoadCalls.Add("owner");

        return Task.FromResult<Result<Guid, Failure>>(
            _profiles.TryGetValue(id, out var profile)
                ? profile.UserId
                : new Failure(Error.NotFound(
                    ApiErrorCodes.BarberProfile.NotFound,
                    $"Barber profile '{id}' was not found.",
                    id)));
    }

    public Task<Result<BarberSalon, Failure>> GetSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        LoadCalls.Add("salon");

        var salon = _profiles.TryGetValue(barberProfileId, out var profile)
            ? profile.Salons.FirstOrDefault(link => link.SalonId == salonId)
            : null;

        if (salon is null)
        {
            return Task.FromResult<Result<BarberSalon, Failure>>(new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.FindSalonNotFound,
                "The barber in the specified salon does not work.",
                salonId)));
        }

        return Task.FromResult<Result<BarberSalon, Failure>>(salon);
    }

    private Task<Result<BarberProfile, Failure>> LoadProfile(string kind, Guid id)
    {
        LoadCalls.Add(kind);

        return Task.FromResult<Result<BarberProfile, Failure>>(
            _profiles.TryGetValue(id, out var profile)
                ? profile
                : new Failure(Error.NotFound(
                    ApiErrorCodes.BarberProfile.NotFound,
                    $"Barber profile '{id}' was not found.",
                    id)));
    }

    public Task<Result<BarberProfile, Failure>> GetForRatingUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<BarberProfile, Failure>>(
            _profiles.TryGetValue(id, out var profile)
                ? profile
                : new Failure(Error.NotFound(
                    ApiErrorCodes.BarberProfile.NotFound,
                    $"Barber profile '{id}' was not found.",
                    id)));

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

    public Task<Result<bool, Failure>> HasSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(
            _profiles.TryGetValue(barberProfileId, out var profile)
            && profile.Salons.Any(link => link.SalonId == salonId));

    public Task<Result<BarberService, Failure>> GetServiceAsync(
        Guid barberProfileId,
        Guid barberServiceId,
        CancellationToken cancellationToken = default)
    {
        var service = _profiles.TryGetValue(barberProfileId, out var profile)
            ? profile.Services.FirstOrDefault(
                x => x.Id == barberServiceId && x.BarberProfileId == barberProfileId)
            : null;

        if (service is null)
        {
            return Task.FromResult<Result<BarberService, Failure>>(new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.ServiceNotFound,
                $"Barber profile '{barberProfileId}' does not provide service '{barberServiceId}'.",
                barberServiceId)));
        }

        return Task.FromResult<Result<BarberService, Failure>>(service);
    }

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
            ApiErrorCodes.BarberProfile.NotFound,
            $"Barber profile '{id}' was not found.",
            id)));
    }

    public Task<Result<BarberSlotContext, Failure>> GetSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (SlotContext is not null && SlotContext.BarberProfileId == barberProfileId)
        {
            return Task.FromResult<Result<BarberSlotContext, Failure>>(SlotContext);
        }

        return Task.FromResult<Result<BarberSlotContext, Failure>>(new Failure(Error.NotFound(
            ApiErrorCodes.Common.BarberSlotsSalonNotFound,
            $"Slot context for barber profile '{barberProfileId}' was not found.",
            barberProfileId)));
    }

    public Task<Result<IReadOnlyList<AdminBarberResponse>, Failure>> GetAdminPageAsync(
        AdminBarberStatus status,
        string? search,
        AdminBarberCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        LastAdminStatus = status;
        LastAdminSearch = search;
        LastAdminCursor = cursor;
        LastAdminLimit = limit;

        var filtered = _adminBarbers.AsEnumerable();

        filtered = status switch
        {
            AdminBarberStatus.Pending => filtered.Where(barber => !barber.IsVerified),
            AdminBarberStatus.Hidden => filtered.Where(barber => !barber.IsActive),
            _ => filtered
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(barber =>
                barber.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || barber.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var descending = status != AdminBarberStatus.Pending;

        filtered = descending
            ? filtered.OrderByDescending(barber => barber.CreatedAt).ThenBy(barber => barber.Id)
            : filtered.OrderBy(barber => barber.CreatedAt).ThenBy(barber => barber.Id);

        if (cursor is { } position)
        {
            filtered = descending
                ? filtered.Where(barber =>
                    barber.CreatedAt < position.CreatedAt
                    || (barber.CreatedAt == position.CreatedAt
                        && barber.Id.CompareTo(position.Id) > 0))
                : filtered.Where(barber =>
                    barber.CreatedAt > position.CreatedAt
                    || (barber.CreatedAt == position.CreatedAt
                        && barber.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<AdminBarberResponse> page = filtered.Take(limit).ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminBarberResponse>, Failure>(page));
    }

    public Task<Result<AdminBarberDetailResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (_adminDetails.TryGetValue(id, out var detail))
        {
            return Task.FromResult<Result<AdminBarberDetailResponse, Failure>>(detail);
        }

        return Task.FromResult<Result<AdminBarberDetailResponse, Failure>>(
            new Failure(Error.NotFound(
                ApiErrorCodes.BarberProfile.NotFound,
                $"Barber profile '{id}' was not found.",
                id)));
    }

    public Task<UnitResult<Failure>> AddAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        var result = AddBehavior?.Invoke(barberProfile) ?? UnitResult.Success<Failure>();

        if (result.IsSuccess)
        {
            _profiles[barberProfile.Id] = barberProfile;
        }

        return Task.FromResult(result);
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        BarberProfile barberProfile,
        CancellationToken cancellationToken = default)
    {
        var result = UpdateBehavior?.Invoke(barberProfile)
            ?? UnitResult.Success<Failure>();

        return Task.FromResult(result);
    }

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

    private static BarberProfile Clone(BarberProfile profile)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (BarberProfile)memberwiseClone.Invoke(profile, null)!;
    }
}
