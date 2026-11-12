using BarberSearch.Application.Bookings;
using BarberSearch.Application.Bookings.Admin;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBookingRepository"/> для тестов обработчиков.
/// </summary>
/// <remarks>
/// Страница и активные брони повторяют фильтры настоящего репозитория, поэтому на фейке
/// проверяются keyset-пагинация и «отменённая бронь слот не занимает». Админская выборка
/// запоминает переданные фильтры, курсор и лимит: тест видит, что обработчик разобрал
/// параметры до обращения к репозиторию.
/// </remarks>
public sealed class FakeBookingRepository : IBookingRepository
{
    private readonly Dictionary<Guid, Booking> _bookings = [];

    private readonly List<AdminBookingResponse> _adminRows = [];

    public IReadOnlyCollection<Booking> Bookings => _bookings.Values;

    /// <summary>
    /// Фильтры последнего админского запроса.
    /// </summary>
    public AdminBookingFilters? LastAdminFilters { get; private set; }

    /// <summary>
    /// Курсор последнего админского запроса.
    /// </summary>
    public AdminBookingCursor? LastAdminCursor { get; private set; }

    /// <summary>
    /// Лимит последнего админского запроса: обработчик читает <c>pageSize + 1</c> строку.
    /// </summary>
    public int LastAdminLimit { get; private set; }

    /// <summary>
    /// Подмена результата админского чтения: тест проверяет, что ошибка репозитория
    /// доходит до контроллера.
    /// </summary>
    public Failure? ForcedAdminFailure { get; set; }

    /// <summary>Ответ <see cref="CountConfirmedAsync"/>: сколько броней подтверждено.</summary>
    public int ConfirmedCount { get; set; }

    /// <summary>Подмена ошибки счётчика подтверждённых броней.</summary>
    public Failure? CountConfirmedFailure { get; set; }

    /// <summary>Сколько раз сводка читала счётчик подтверждённых броней.</summary>
    public int CountConfirmedCallCount { get; private set; }

    /// <summary>
    /// Хук перед счётчиком подтверждённых броней: тест сводки проверяет порядок
    /// последовательных чтений.
    /// </summary>
    public Action? BeforeCountConfirmed { get; set; }

    /// <summary>
    /// Подмена поведения вставки — для проверки гонки, которую в памяти не воспроизвести.
    /// </summary>
    public Func<Booking, UnitResult<Failure>>? OnAdd { get; set; }

    /// <summary>
    /// Подмена поведения сохранения — для проверки проигранной оптимистической блокировки
    /// (<c>booking.update.concurrencyConflict</c>), которую создаёт реальный <c>xmin</c>.
    /// </summary>
    public Func<Booking, UnitResult<Failure>>? OnUpdate { get; set; }

    /// <summary>
    /// Подмена поведения взятия advisory-блокировки — для проверки истёкшего
    /// <c>lock_timeout</c> (<c>booking.add.lockTimeout</c>, R-18).
    /// </summary>
    public Func<Guid, UnitResult<Failure>>? OnAcquireLock { get; set; }

    public int AddCalls { get; private set; }

    public int LockCalls { get; private set; }

    public Guid? LockedBarberProfileId { get; private set; }

    /// <summary>
    /// Порядок вызовов репозитория: тест проверяет, что блокировка берётся до вставки (R-15).
    /// </summary>
    public List<string> Operations { get; } = [];

    public int UpdateCalls { get; private set; }

    public int GetByIdempotencyKeyCalls { get; private set; }

    public void Add(Booking booking) => _bookings[booking.Id] = booking;

    public Task<UnitResult<Failure>> AcquireBarberLockAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        LockCalls++;
        LockedBarberProfileId = barberProfileId;
        Operations.Add("lock");

        return Task.FromResult(
            OnAcquireLock?.Invoke(barberProfileId) ?? UnitResult.Success<Failure>());
    }

    public Task<Result<Booking, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<Booking, Failure>>(
            _bookings.TryGetValue(id, out var booking)
                ? booking
                : new Failure(Error.NotFound(
                    ApiErrorCodes.Booking.NotFound,
                    $"Booking '{id}' was not found.",
                    id)));

    public Task<Result<Booking, Failure>> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        GetByIdempotencyKeyCalls++;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.Validation(
                ApiErrorCodes.Booking.IdempotencyKeyInvalid,
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey))));
        }

        var key = idempotencyKey.Trim();
        var booking = _bookings.Values.FirstOrDefault(
            x => x.UserId == userId && x.IdempotencyKey == key);

        if (booking is null)
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.NotFound(
                ApiErrorCodes.Booking.IdempotencyKeyNotFound,
                $"Booking with idempotency key '{key}' was not found for user '{userId}'.")));
        }

        return Task.FromResult<Result<Booking, Failure>>(booking);
    }

    public Task<Result<Booking, Failure>> GetByGuestIdempotencyKeyAsync(
        Guid barberProfileId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        GetByIdempotencyKeyCalls++;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.Validation(
                ApiErrorCodes.Booking.IdempotencyKeyInvalid,
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey))));
        }

        var key = idempotencyKey.Trim();
        var booking = _bookings.Values.FirstOrDefault(
            x => x.UserId == null
                 && x.BarberProfileId == barberProfileId
                 && x.IdempotencyKey == key);

        if (booking is null)
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.NotFound(
                ApiErrorCodes.Booking.IdempotencyKeyNotFound,
                $"Guest booking with idempotency key '{key}' was not found.")));
        }

        return Task.FromResult<Result<Booking, Failure>>(booking);
    }

    public Task<Result<IReadOnlyList<Booking>, Failure>> GetByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<BookingResponse>, Failure>> GetPageAsync(
        Guid? userId,
        BookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Booking> query = _bookings.Values;

        if (userId is { } filterUserId)
        {
            query = query.Where(booking => booking.UserId == filterUserId);
        }

        // Тот же фильтр, что и в настоящем репозитории: скоуп решается временем окончания,
        // а не только статусом.
        query = scope switch
        {
            BookingScope.Upcoming => query.Where(booking =>
                (booking.Status == BookingStatus.Pending || booking.Status == BookingStatus.Confirmed)
                && booking.EndAt > now),
            BookingScope.Past => query.Where(booking =>
                (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
                || booking.EndAt <= now),
            _ => query
        };

        if (cursor is { } position)
        {
            query = scope switch
            {
                BookingScope.Upcoming => query.Where(booking =>
                    booking.StartAt > position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) > 0)),
                BookingScope.Past => query.Where(booking =>
                    booking.StartAt < position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) < 0)),
                _ => query.Where(booking =>
                    booking.StartAt < position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) > 0))
            };
        }

        query = scope switch
        {
            BookingScope.Upcoming => query
                .OrderBy(booking => booking.StartAt)
                .ThenBy(booking => booking.Id),
            BookingScope.Past => query
                .OrderByDescending(booking => booking.StartAt)
                .ThenByDescending(booking => booking.Id),
            _ => query
                .OrderByDescending(booking => booking.StartAt)
                .ThenBy(booking => booking.Id)
        };

        IReadOnlyList<BookingResponse> rows = query
            .Take(limit)
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<BookingResponse>, Failure>(rows));
    }

    public Task<Result<IReadOnlyList<BarberBookingResponse>, Failure>> GetBarberPageAsync(
        Guid barberProfileId,
        BarberBookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Booking> query = _bookings.Values
            .Where(booking => booking.BarberProfileId == barberProfileId);

        // Тот же фильтр, что и в настоящем репозитории: скоуп решается временем окончания,
        // а не только статусом.
        query = scope == BarberBookingScope.Upcoming
            ? query.Where(booking =>
                (booking.Status == BookingStatus.Pending || booking.Status == BookingStatus.Confirmed)
                && booking.EndAt > now)
            : query.Where(booking =>
                (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
                || booking.EndAt <= now);

        if (cursor is { } position)
        {
            query = scope == BarberBookingScope.Upcoming
                ? query.Where(booking =>
                    booking.StartAt > position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) > 0))
                : query.Where(booking =>
                    booking.StartAt < position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) < 0));
        }

        query = scope == BarberBookingScope.Upcoming
            ? query.OrderBy(booking => booking.StartAt).ThenBy(booking => booking.Id)
            : query.OrderByDescending(booking => booking.StartAt).ThenByDescending(booking => booking.Id);

        IReadOnlyList<BarberBookingResponse> rows = query
            .Take(limit)
            .Select(ToBarberResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<BarberBookingResponse>, Failure>(rows));
    }

    /// <summary>
    /// Добавляет строку админского списка.
    /// </summary>
    public void AddAdminRow(AdminBookingResponse row) => _adminRows.Add(row);

    public Task<Result<IReadOnlyList<AdminBookingResponse>, Failure>> GetAdminPageAsync(
        AdminBookingFilters filters,
        AdminBookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        LastAdminFilters = filters;
        LastAdminCursor = cursor;
        LastAdminLimit = limit;

        if (ForcedAdminFailure is not null)
        {
            return Task.FromResult(
                Result.Failure<IReadOnlyList<AdminBookingResponse>, Failure>(
                    ForcedAdminFailure));
        }

        IEnumerable<AdminBookingResponse> query = _adminRows;

        if (filters.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }

        if (filters.Search is { } search)
        {
            // Повторяем правило репозитория: GUID — точный id, иначе подстрока email.
            // У гостя email нет: подстрокой он не находится, но в списке остаётся.
            query = Guid.TryParse(search, out var searchedId)
                ? query.Where(row => row.Id == searchedId)
                : query.Where(row => row.Customer.Email is not null
                    && row.Customer.Email.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (cursor is { } position)
        {
            query = query.Where(row =>
                row.StartAt < position.StartAt
                || (row.StartAt == position.StartAt && row.Id.CompareTo(position.Id) > 0));
        }

        var page = query
            .OrderByDescending(row => row.StartAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminBookingResponse>, Failure>(page));
    }

    public Task<Result<AdminBookingResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (ForcedAdminFailure is not null)
        {
            return Task.FromResult(
                Result.Failure<AdminBookingResponse, Failure>(ForcedAdminFailure));
        }

        var row = _adminRows.FirstOrDefault(candidate => candidate.Id == id);

        return Task.FromResult(
            row is null
                ? Result.Failure<AdminBookingResponse, Failure>(new Failure(Error.NotFound(
                    ApiErrorCodes.AdminBooking.NotFound,
                    $"Booking '{id}' was not found.",
                    id)))
                : Result.Success<AdminBookingResponse, Failure>(row));
    }

    /// <summary>
    /// Счётчик подтверждённых броней; тест задаёт значение и ошибку явно.
    /// </summary>
    public Task<Result<int, Failure>> CountConfirmedAsync(
        CancellationToken cancellationToken = default)
    {
        CountConfirmedCallCount++;
        BeforeCountConfirmed?.Invoke();

        return Task.FromResult(CountConfirmedFailure is { } failure
            ? Result.Failure<int, Failure>(failure)
            : Result.Success<int, Failure>(ConfirmedCount));
    }

    public Task<Result<IReadOnlyList<Booking>, Failure>> GetActiveByBarberAsync(
        Guid barberProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Booking> rows = _bookings.Values
            .Where(booking => booking.BarberProfileId == barberProfileId)
            .Where(booking => booking.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            .Where(booking => booking.StartAt < to && from < booking.EndAt)
            .OrderBy(booking => booking.StartAt)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<Booking>, Failure>(rows));
    }

    public Task<Result<bool, Failure>> HasActiveOverlapAsync(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default)
    {
        var overlaps = _bookings.Values.Any(booking =>
            booking.BarberProfileId == barberProfileId
            && booking.Status is BookingStatus.Pending or BookingStatus.Confirmed
            && booking.StartAt < endAt
            && startAt < booking.EndAt);

        return Task.FromResult(Result.Success<bool, Failure>(overlaps));
    }

    public Task<UnitResult<Failure>> AddAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
    {
        AddCalls++;
        Operations.Add("add");

        if (OnAdd is not null)
        {
            return Task.FromResult(OnAdd(booking));
        }

        // Повторяем правила БД, чтобы тесты видели те же коды, что и на PostgreSQL.
        // Клиент — (user_id, key), гость — (barber_profile_id, key) при user_id IS NULL.
        var duplicate = booking.UserId is { } userId
            ? _bookings.Values.Any(x =>
                x.UserId == userId
                && x.IdempotencyKey == booking.IdempotencyKey)
            : _bookings.Values.Any(x =>
                x.UserId == null
                && x.BarberProfileId == booking.BarberProfileId
                && x.IdempotencyKey == booking.IdempotencyKey);

        if (duplicate)
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                ApiErrorCodes.Booking.AddUniqueViolation,
                "A record with the same unique values already exists."))));
        }

        if (_bookings.Values.Any(x =>
                x.BarberProfileId == booking.BarberProfileId
                && x.Status is BookingStatus.Pending or BookingStatus.Confirmed
                && x.StartAt < booking.EndAt
                && booking.StartAt < x.EndAt))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                ApiErrorCodes.Booking.AddOverlap,
                "The period overlaps with an existing record."))));
        }

        _bookings[booking.Id] = booking;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        return Task.FromResult(OnUpdate?.Invoke(booking) ?? UnitResult.Success<Failure>());
    }

    private static BookingResponse ToResponse(Booking booking)
        => new(
            booking.Id,
            booking.UserId,
            booking.BarberProfileId,
            booking.BarberServiceId,
            booking.SalonId,
            booking.StartAt,
            booking.EndAt,
            booking.Price,
            booking.Status,
            booking.IdempotencyKey,
            booking.CustomerNote,
            booking.BarberNote,
            booking.CancellationReason,
            booking.CreatedAt,
            HasReview: false);

    /// <summary>
    /// Запись барбера с синтетическим клиентом: фейк проверяет фильтры и порядок,
    /// а имена и телефоны приходят из базы в интеграционных тестах. У гостя аккаунта нет,
    /// поэтому имя и телефон берутся из снимка брони.
    /// </summary>
    private static BarberBookingResponse ToBarberResponse(Booking booking)
        => new(
            booking.Id,
            booking.Status,
            booking.StartAt,
            booking.EndAt,
            booking.Price,
            booking.CustomerNote,
            booking.BarberNote,
            booking.CancellationReason,
            new BarberBookingCustomerResponse(
                booking.UserId is { } userId
                    ? $"Customer {userId:N}"
                    : $"{booking.GuestFirstName} {booking.GuestLastName}".Trim(),
                booking.UserId is null ? booking.GuestPhone : null,
                AvatarUrl: null),
            new BarberBookingServiceResponse($"Service {booking.BarberServiceId:N}"),
            new BarberBookingSalonResponse("Fake salon", "Fake address", "Fake city", "Europe/Moscow"));
}
