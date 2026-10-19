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
        => throw new NotSupportedException();

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
