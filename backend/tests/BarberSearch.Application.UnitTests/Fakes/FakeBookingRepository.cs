using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
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
/// проверяются keyset-пагинация и «отменённая бронь слот не занимает».
/// </remarks>
public sealed class FakeBookingRepository : IBookingRepository
{
    private readonly Dictionary<Guid, Booking> _bookings = [];

    public IReadOnlyCollection<Booking> Bookings => _bookings.Values;

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
        if (_bookings.Values.Any(x =>
                x.UserId == booking.UserId
                && x.IdempotencyKey == booking.IdempotencyKey))
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
            booking.CreatedAt);

    /// <summary>
    /// Запись барбера с синтетическим клиентом: фейк проверяет фильтры и порядок,
    /// а имена и телефоны приходят из базы в интеграционных тестах.
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
            new BarberBookingCustomerResponse($"Customer {booking.UserId:N}", null),
            new BarberBookingServiceResponse($"Service {booking.BarberServiceId:N}"),
            new BarberBookingSalonResponse("Fake salon", "Fake address", "Fake city", "Europe/Moscow"));
}
