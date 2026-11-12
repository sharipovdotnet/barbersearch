using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Photos.Admin;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
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
    private readonly List<AdminPortfolioPhotoResponse> _adminPortfolioRows = [];
    private readonly Dictionary<Guid, AdminBarberDetailResponse> _adminDetails = [];
    private readonly List<BarberSalonSummaryResponse> _salonSummaries = [];
    private readonly List<SalonBarberResponse> _salonBarbers = [];
    private readonly List<SalonBarberResponse> _salonBarberCandidates = [];
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

    /// <summary>Ответ <see cref="CountPendingAsync"/>: неподтверждённых профилей в сценарии.</summary>
    public int PendingCount { get; set; }

    /// <summary>Подмена ошибки счётчика очереди проверки.</summary>
    public Failure? CountPendingFailure { get; set; }

    /// <summary>Ответ <see cref="CountPendingPortfolioPhotosAsync"/>: работ на проверке.</summary>
    public int PendingPortfolioCount { get; set; }

    /// <summary>Подмена ошибки счётчика очереди портфолио.</summary>
    public Failure? CountPendingPortfolioFailure { get; set; }

    /// <summary>Сколько раз сводка читала счётчик портфолио.</summary>
    public int CountPendingPortfolioCallCount { get; private set; }

    /// <summary>
    /// Хук перед счётчиком портфолио: тест сводки проверяет порядок последовательных чтений.
    /// </summary>
    public Action? BeforeCountPendingPortfolioPhotos { get; set; }

    /// <summary>
    /// Поиск, курсор и лимит последнего запроса очереди портфолио: тесты проверяют, что
    /// обработчик передал обрезанную строку, разобранный курсор и pageSize + 1.
    /// </summary>
    public string? LastPortfolioPhotoSearch { get; private set; }

    public AdminPhotoCursor? LastPortfolioPhotoCursor { get; private set; }

    public int? LastPortfolioPhotoLimit { get; private set; }

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

    /// <summary>Сколько раз обработчик сохранял профиль через <see cref="UpdateAsync"/>.</summary>
    public int UpdateCallCount { get; private set; }

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

    /// <summary>Строка «Мои салоны» для сценариев чтения привязок.</summary>
    public void AddSalonSummary(BarberSalonSummaryResponse summary) => _salonSummaries.Add(summary);

    /// <summary>Строка «Барберы салона» для сценария управления составом.</summary>
    public void AddSalonBarber(SalonBarberResponse barber) => _salonBarbers.Add(barber);

    /// <summary>Ответ подсказки поиска барбера для салона.</summary>
    public void AddSalonBarberCandidate(SalonBarberResponse barber)
        => _salonBarberCandidates.Add(barber);

    /// <summary>Строка поиска, которую обработчик передал в репозиторий.</summary>
    public string? LastSalonBarberSearch { get; private set; }

    /// <summary>Лимит, который обработчик передал в репозиторий.</summary>
    public int? LastSalonBarberLimit { get; private set; }

    public void AddCard(BarberProfileResponse card) => _cards[card.Id] = card;

    public void AddAdminBarber(AdminBarberResponse barber) => _adminBarbers.Add(barber);

    /// <summary>
    /// Строка очереди портфолио: фейк хранит уже готовые строки, потому что фильтр Pending
    /// и joins выполняет настоящий SQL.
    /// </summary>
    public void AddAdminPortfolioPhotoRow(AdminPortfolioPhotoResponse row)
        => _adminPortfolioRows.Add(row);

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

    public Task<Result<BarberProfile, Failure>> GetWithScheduleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => LoadProfile("schedule", id);

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

    public Task<Result<IReadOnlyList<BarberSalonSummaryResponse>, Failure>> GetSalonSummariesAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        LoadCalls.Add("salonSummaries");

        if (!_profiles.ContainsKey(barberProfileId))
        {
            return Task.FromResult<Result<IReadOnlyList<BarberSalonSummaryResponse>, Failure>>(
                new Failure(Error.NotFound(
                    ApiErrorCodes.BarberProfile.NotFound,
                    $"Barber profile '{barberProfileId}' was not found.",
                    barberProfileId)));
        }

        var ordered = _salonSummaries
            .OrderByDescending(row => row.IsPrimary)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.SalonId)
            .ToList();

        return Task.FromResult<Result<IReadOnlyList<BarberSalonSummaryResponse>, Failure>>(
            ordered);
    }

    public Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>> GetSalonBarbersAsync(
        Guid salonId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<IReadOnlyList<SalonBarberResponse>, Failure>>(
            _salonBarbers
                .OrderBy(barber => barber.DisplayName, StringComparer.Ordinal)
                .ThenBy(barber => barber.BarberProfileId)
                .ToList());

    public Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>>
        SearchSalonBarberCandidatesAsync(
            Guid salonId,
            string search,
            int limit,
            CancellationToken cancellationToken = default)
    {
        LastSalonBarberSearch = search;
        LastSalonBarberLimit = limit;

        IReadOnlyList<SalonBarberResponse> rows = _salonBarberCandidates
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<SalonBarberResponse>, Failure>(rows));
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

    /// <summary>
    /// Данные фотографии по паре (профиль, URL). Фейк ищет строку в загруженном агрегате:
    /// фильтры видимости проверяет вызывающий код, как и в настоящем репозитории.
    /// </summary>
    public Task<PortfolioPhotoAccess?> GetPortfolioPhotoAccessAsync(
        Guid barberProfileId,
        string url,
        CancellationToken cancellationToken = default)
    {
        var access = _profiles.TryGetValue(barberProfileId, out var profile)
            ? profile.PortfolioPhotos
                .Where(photo => photo.Url == url)
                .Select(photo => new PortfolioPhotoAccess(
                    profile.UserId,
                    photo.IsPublished,
                    photo.IsBlocked,
                    photo.ModerationStatus))
                .FirstOrDefault()
            : null;

        return Task.FromResult(access);
    }

    public Task<Result<bool, Failure>> HasSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<bool, Failure>>(
            _profiles.TryGetValue(barberProfileId, out var profile)
            && profile.Salons.Any(link => link.SalonId == salonId));

    /// <summary>
    /// Профили, которые поиск по дате исключает из-за отпуска на весь локальный день.
    /// Ответ репозитория, а не его логика: сам расчёт проверяется интеграционным тестом.
    /// </summary>
    public IReadOnlyList<Guid> BlockedProfileIds { get; set; } = [];

    public int GetDayBlockedProfileIdsCalls { get; private set; }

    public Task<Result<IReadOnlyList<Guid>, Failure>> GetDayBlockedProfileIdsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        GetDayBlockedProfileIdsCalls++;

        return Task.FromResult(
            Result.Success<IReadOnlyList<Guid>, Failure>(BlockedProfileIds));
    }

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

    /// <summary>
    /// Подсказки фильтра «дополнительные услуги», которые вернёт фейк.
    /// </summary>
    public IReadOnlyList<string> AdditionalServices { get; set; } = [];

    public Task<Result<IReadOnlyList<string>, Failure>> GetAdditionalServicesAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            Result.Success<IReadOnlyList<string>, Failure>(AdditionalServices));

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

    /// <summary>
    /// Собственная карточка: в фейке совпадает с публичной — разницу фильтров проверяет
    /// интеграционный тест на PostgreSQL, а обработчик проверяет владение и коды ошибок.
    /// </summary>
    public Task<Result<BarberProfileResponse, Failure>> GetOwnCardByIdAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
        => GetCardByIdAsync(barberProfileId, cancellationToken);

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

    /// <summary>
    /// Собственные слоты в фейке совпадают с публичными: тесты обработчика проверяют
    /// выбор метода по флагу, а не разницу расчёта.
    /// </summary>
    public Task<Result<BarberSlotContext, Failure>> GetOwnSlotContextAsync(
        Guid barberProfileId,
        Guid? salonId,
        Guid? hairServiceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => GetSlotContextAsync(
            barberProfileId,
            salonId,
            hairServiceId,
            date,
            cancellationToken);

    /// <summary>
    /// Данные формы ручной записи; тест задаёт их явно, чтобы не собирать карточку.
    /// </summary>
    public BarberBookingOptionsResponse? BookingOptions { get; set; }

    public Task<Result<BarberBookingOptionsResponse, Failure>> GetBookingOptionsAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<BarberBookingOptionsResponse, Failure>>(
            BookingOptions is { } options
                ? options
                : new Failure(Error.NotFound(
                    ApiErrorCodes.BarberProfile.NotFound,
                    $"Barber profile '{barberProfileId}' was not found.",
                    barberProfileId)));

    public Task<Result<int, Failure>> CountPendingAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(CountPendingFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(PendingCount));

    /// <summary>
    /// Очередь портфолио в памяти: FIFO по <c>created_at, id</c>, тот же поиск по имени
    /// барбера и email владельца, что у PostgreSQL.
    /// </summary>
    public Task<Result<IReadOnlyList<AdminPortfolioPhotoResponse>, Failure>>
        GetAdminPortfolioPhotosPageAsync(
            AdminPhotoCursor? cursor,
            string? search,
            int limit,
            CancellationToken cancellationToken = default)
    {
        LastPortfolioPhotoSearch = search;
        LastPortfolioPhotoCursor = cursor;
        LastPortfolioPhotoLimit = limit;

        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminPortfolioPhotoResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.AdminPhotos.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        IEnumerable<AdminPortfolioPhotoResponse> query = _adminPortfolioRows;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();

            query = query.Where(row =>
                row.BarberDisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.OwnerEmail.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (cursor is { } position)
        {
            query = query.Where(row =>
                row.SubmittedAt > position.SubmittedAt
                || (row.SubmittedAt == position.SubmittedAt
                    && row.PhotoId.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<AdminPortfolioPhotoResponse> rows = query
            .OrderBy(row => row.SubmittedAt)
            .ThenBy(row => row.PhotoId)
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminPortfolioPhotoResponse>, Failure>(rows));
    }

    /// <summary>
    /// Счётчик работ на проверке; тест задаёт значение и ошибку явно.
    /// </summary>
    public Task<Result<int, Failure>> CountPendingPortfolioPhotosAsync(
        CancellationToken cancellationToken = default)
    {
        CountPendingPortfolioCallCount++;
        BeforeCountPendingPortfolioPhotos?.Invoke();

        return Task.FromResult(CountPendingPortfolioFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(PendingPortfolioCount));
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
        UpdateCallCount++;

        var result = UpdateBehavior?.Invoke(barberProfile)
            ?? UnitResult.Success<Failure>();

        if (result.IsSuccess)
        {
            _profiles[barberProfile.Id] = barberProfile;
        }

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
