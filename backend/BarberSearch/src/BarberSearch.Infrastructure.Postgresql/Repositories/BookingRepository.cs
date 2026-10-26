using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Booking"/>.
/// </summary>
public sealed class BookingRepository : RepositoryBase<Booking>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
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
            return NotFound(ApiErrorCodes.Booking.NotFound, nameof(Booking), id);
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
                ApiErrorCodes.Booking.IdempotencyKeyInvalid,
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey)));
        }

        var key = idempotencyKey.Trim();

        // Обработчику нужен только идентификатор существующей брони, поэтому чтение
        // идёт без отслеживания (AGENT.md §15.6).
        var booking = await Context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKey == key,
                cancellationToken);

        if (booking is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Booking.IdempotencyKeyNotFound,
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
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Booking.PagingPageInvalid,
                ApiErrorCodes.Booking.PagingPageSizeInvalid) is { } pagingError)
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

    public async Task<Result<IReadOnlyList<BookingResponse>, Failure>> GetPageAsync(
        Guid? userId,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.PageLimitInvalid,
                "Page limit must be positive.",
                nameof(limit)));
        }

        var query = Context.Bookings.AsNoTracking();

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

        var rows = await query
            .OrderByDescending(booking => booking.StartAt)
            .ThenBy(booking => booking.Id)
            .Take(limit)
            .Select(booking => new BookingResponse(
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
                booking.CreatedAt))
            .ToListAsync(cancellationToken);

        return rows;
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
                ApiErrorCodes.Booking.ActivePeriodInvalid,
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
                ApiErrorCodes.Booking.OverlapPeriodInvalid,
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

    public async Task<UnitResult<Failure>> AcquireBarberLockAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        // 50 concurrent inserts on one slot make PostgreSQL's GiST exclusion check
        // deadlock (40P01) before either transaction can commit, and under load all
        // contenders can lose, so no 201 is produced at all. Serialising inserts for
        // one barber with a transaction-scoped advisory lock makes the exclusion
        // constraint the only decider: first insert wins, the rest get 23P01 -> 409.
        // Booking create is low-volume (§15.1), so per-barber serialisation is cheap.
        //
        // Lock lives until the end of the caller's transaction, so this method must be
        // called inside IUnitOfWork.ExecuteAsync. It is deliberately narrow: the
        // repository neither starts nor commits the transaction (R-15).
        //
        // SET LOCAL действует только до конца текущей транзакции и только на это
        // соединение, поэтому read-запросы без транзакции его не получают. Без
        // ограничения pg_advisory_xact_lock ждал бы бесконечно: зависшая транзакция
        // держала бы все новые брони барбера и копила запросы в пуле (R-18).
        try
        {
            await Context.Database.ExecuteSqlRawAsync(
                $"SET LOCAL lock_timeout = '{PostgresSettings.LOCK_TIMEOUT}'",
                cancellationToken);

            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 SELECT pg_advisory_xact_lock(
                     hashtextextended({barberProfileId.ToString()}, 0))
                 """,
                cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // 55P03: блокировку не удалось взять за lock_timeout. Транзакция уже
            // прервана, её откатит IUnitOfWork; клиенту отдаём 409, а не 500.
            return UnitResult.Failure(MapPostgresError(exception, ApiErrorCodes.Booking.AddPrefix));
        }
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        try
        {
            await Context.Bookings.AddAsync(booking, cancellationToken);

            // Повтор ключа идемпотентности ловит уникальный индекс
            // ux_bookings_user_id_idempotency_key, пересечение интервалов — exclusion-ограничение
            // ex_bookings_no_overlap. Оба случая маппятся в Error.Conflict.
            //
            // Транзакцию репозиторий не открывает: её границей владеет IUnitOfWork,
            // поэтому вставка не может закоммититься раньше проверок обработчика.
            var result = await SaveChangesAsync(ApiErrorCodes.Booking.AddPrefix, cancellationToken);

            if (result.IsFailure)
            {
                // Неудачная вставка не должна остаться в change tracker: обработчик затем
                // перечитывает бронь по ключу, а случайный повторный SaveChanges в том же
                // запросе снова наткнулся бы на то же ограничение.
                Context.Entry(booking).State = EntityState.Detached;
            }

            return result;
        }
        catch
        {
            // Исключение уже откатит транзакцию IUnitOfWork; здесь важно только не оставить
            // бронь в трекере, иначе она может быть записана следующим SaveChanges.
            Context.Entry(booking).State = EntityState.Detached;

            throw;
        }
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(booking, ApiErrorCodes.Booking.UpdatePrefix, cancellationToken);
}
