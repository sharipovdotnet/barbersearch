using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.GetBookings;
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

    public int AddCalls { get; private set; }

    public void Add(Booking booking) => _bookings[booking.Id] = booking;

    public Task<Result<Booking, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<Booking, Failure>>(
            _bookings.TryGetValue(id, out var booking)
                ? booking
                : new Failure(Error.NotFound(
                    "booking.getById.notFound",
                    $"Booking '{id}' was not found.",
                    id)));

    public Task<Result<Booking, Failure>> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.Validation(
                "booking.getIdempotencyKey.idempotencyKey",
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey))));
        }

        var key = idempotencyKey.Trim();
        var booking = _bookings.Values.FirstOrDefault(
            x => x.UserId == userId && x.IdempotencyKey == key);

        if (booking is null)
        {
            return Task.FromResult<Result<Booking, Failure>>(new Failure(Error.NotFound(
                "booking.getIdempotencyKey.notFound",
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
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Booking> query = _bookings.Values;

        if (userId is { } filterUserId)
        {
            query = query.Where(booking => booking.UserId == filterUserId);
        }

        if (cursor is { } position)
        {
            query = query.Where(booking =>
                booking.StartAt < position.StartAt
                || (booking.StartAt == position.StartAt
                    && booking.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<BookingResponse> rows = query
            .OrderByDescending(booking => booking.StartAt)
            .ThenBy(booking => booking.Id)
            .Take(limit)
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<BookingResponse>, Failure>(rows));
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
                "booking.add.uniqueViolation",
                "A record with the same unique values already exists."))));
        }

        if (_bookings.Values.Any(x =>
                x.BarberProfileId == booking.BarberProfileId
                && x.Status is BookingStatus.Pending or BookingStatus.Confirmed
                && x.StartAt < booking.EndAt
                && booking.StartAt < x.EndAt))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                "booking.add.overlap",
                "The period overlaps with an existing record."))));
        }

        _bookings[booking.Id] = booking;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UnitResult.Success<Failure>());

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
}
