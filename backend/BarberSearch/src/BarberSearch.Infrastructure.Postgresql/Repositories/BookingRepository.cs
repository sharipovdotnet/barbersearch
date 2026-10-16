using BarberSearch.Application.Bookings;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Booking"/>.
/// </summary>
public sealed class BookingRepository : RepositoryBase<Booking>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Result<Booking, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var booking = await Context.Bookings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (booking is null)
        {
            return NotFound("booking.getById.notFound", nameof(Booking), id);
        }

        return booking;
    }

    public async Task<Result<Booking, Failure>> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new Failure(Error.Validation(
                "booking.getIdempotencyKey.idempotencyKey",
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey)));
        }

        var key = idempotencyKey.Trim();

        var booking = await Context.Bookings
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKey == key,
                cancellationToken);

        if (booking is null)
        {
            return new Failure(Error.NotFound(
                "booking.getIdempotencyKey.notFound",
                $"Booking with idempotency key '{key}' was not found for user '{userId}'."));
        }

        return booking;
    }

    public async Task<Result<IReadOnlyList<Booking>, Failure>> GetByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(page, pageSize, "booking.getByUser") is { } pagingError)
        {
            return pagingError;
        }

        var bookings = await Context.Bookings
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StartAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return bookings;
    }

    public async Task<Result<IReadOnlyList<Booking>, Failure>> GetActiveByBarberAsync(
        Guid barberProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            return new Failure(Error.Validation(
                "booking.getActiveByBarber.period",
                "The end of the period must be after its start.",
                nameof(to)));
        }

        var bookings = await Context.Bookings
            .AsNoTracking()
            // Отменённые, завершённые и неявившиеся слот не занимают: тот же фильтр,
            // что и в частичном индексе ix_bookings_barber_profile_id_start_at_active.
            .Where(x => x.BarberProfileId == barberProfileId
                        && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
                        && x.StartAt < to
                        && from < x.EndAt)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        return bookings;
    }

    public async Task<Result<bool, Failure>> HasActiveOverlapAsync(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default)
    {
        if (endAt <= startAt)
        {
            return new Failure(Error.Validation(
                "booking.hasActiveOverlap.period",
                "End time must be after start time.",
                nameof(endAt)));
        }

        var overlaps = await Context.Bookings
            .AsNoTracking()
            .AnyAsync(
                x => x.BarberProfileId == barberProfileId
                     && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
                     && x.StartAt < endAt
                     && startAt < x.EndAt,
                cancellationToken);

        return overlaps;
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        await Context.Bookings.AddAsync(booking, cancellationToken);

        // Повтор ключа идемпотентности поймает уникальный индекс
        // ux_bookings_user_id_idempotency_key, пересечение интервалов — exclusion-ограничение
        // (когда оно появится в миграции). Оба случая вернут Error.Conflict.
        return await SaveChangesAsync("booking.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(booking, "booking.update", cancellationToken);
}
