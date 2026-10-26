using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Review"/>.
/// </summary>
public sealed class ReviewRepository : RepositoryBase<Review>, IReviewRepository
{
    public ReviewRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<Review, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var review = await Context.Reviews
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (review is null)
        {
            return NotFound(ApiErrorCodes.Review.NotFound, nameof(Review), id);
        }

        return review;
    }

    public async Task<Result<Review, Failure>> GetByBookingIdAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var review = await Context.Reviews
            .FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);

        if (review is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Review.NotFound,
                $"Review for booking '{bookingId}' was not found.",
                bookingId));
        }

        return review;
    }

    /// <summary>
    /// Страница отзывов барбера, свежие сверху. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать
    /// <see cref="MAX_PAGE_OFFSET"/> (10 000), иначе <c>400</c> с кодом
    /// <c>review.paging.depth</c> — до обращения к базе.
    /// </remarks>
    public async Task<Result<IReadOnlyList<Review>, Failure>> GetByBarberProfileIdAsync(
        Guid barberProfileId,
        int page,
        int pageSize,
        bool onlyPublished = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Review.PagingPageInvalid,
                ApiErrorCodes.Review.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        if (ValidatePageOffset(
                page,
                pageSize,
                ApiErrorCodes.Review.PagingDepthInvalid) is { } depthError)
        {
            return depthError;
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(x => x.BarberProfileId == barberProfileId);

        if (onlyPublished)
        {
            query = query.Where(x => x.IsPublished);
        }

        // План индекса ix_reviews_barber_profile_id_created_at: свежие сверху.
        var reviews = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return reviews;
    }

    public async Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetPublishedPageAsync(
        Guid barberProfileId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                "Page limit must be positive.",
                nameof(limit)));
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt < position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        var rows = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(review => new ReviewResponse(
                review.Id,
                review.BarberProfileId,
                review.UserId,
                review.BookingId,
                review.Rating,
                review.Text,
                review.IsPublished,
                review.CreatedAt,
                review.UpdatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Очередь модерации: скрытые отзывы всех барберов, старые сверху (FIFO).
    /// </summary>
    /// <remarks>
    /// Порядок <c>created_at ASC, id</c> обслуживает частичный индекс
    /// <c>ix_reviews_moderation_created_at_id</c> (<c>WHERE is_published = false</c>).
    /// Курсор хранит пару <c>(created_at, id)</c>: условие продолжения обратно предикату
    /// карточки, потому что очередь читается по возрастанию. Опубликованные отзывы в выдачу
    /// не попадают. <paramref name="limit"/> &lt;= 0 даёт <c>400 review.page.limit</c> до SQL.
    /// </remarks>
    public async Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetModerationPageAsync(
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                "Page limit must be positive.",
                nameof(limit)));
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(review => !review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt > position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        var rows = await query
            .OrderBy(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(review => new ReviewResponse(
                review.Id,
                review.BarberProfileId,
                review.UserId,
                review.BookingId,
                review.Rating,
                review.Text,
                review.IsPublished,
                review.CreatedAt,
                review.UpdatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var summary = await BuildRatingSummaryQuery(Context, barberProfileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (summary is null || summary.Count == 0)
        {
            return new ReviewRatingSummary(null, 0);
        }

        return summary;
    }

    /// <summary>
    /// Строит один агрегирующий запрос сводки по опубликованным отзывам.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CountAsync</c> + <c>AverageAsync</c> — это два round-trip к <c>reviews</c>.
    /// Сводка читается, пока <c>ReviewRatingRecalculator</c> держит
    /// <c>SELECT … FOR UPDATE</c> на строке профиля, поэтому второй запрос удлиняет
    /// блокировку; здесь <c>COUNT</c> и <c>AVG</c> возвращаются одним SQL.
    /// </para>
    /// <para>
    /// Пустая выборка не даёт строки, и <c>FirstOrDefaultAsync</c> возвращает <c>null</c>;
    /// округление среднего по-прежнему делает домен (<c>BarberProfile.UpdateRating</c>).
    /// </para>
    /// </remarks>
    /// <param name="context">Контекст базы данных.</param>
    /// <param name="barberProfileId">Профиль, чьи отзывы агрегируются.</param>
    /// <returns>Запрос, материализующий сводку либо <c>null</c> при отсутствии отзывов.</returns>
    internal static IQueryable<ReviewRatingSummary> BuildRatingSummaryQuery(
        ApplicationDbContext context,
        Guid barberProfileId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Reviews
            .AsNoTracking()
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished)
            .GroupBy(_ => 1)
            .Select(group => new ReviewRatingSummary(
                group.Average(review => (decimal?)review.Rating),
                group.Count()));
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        await Context.Reviews.AddAsync(review, cancellationToken);

        // Второй отзыв на ту же бронь поймает уникальный индекс ux_reviews_booking_id.
        return await SaveChangesAsync(ApiErrorCodes.Review.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Review review,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(review, ApiErrorCodes.Review.UpdatePrefix, cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        Context.Reviews.Remove(review);

        return await SaveChangesAsync(ApiErrorCodes.Review.DeletePrefix, cancellationToken);
    }
}
