using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Review"/>.
/// </summary>
public sealed class ReviewRepository : RepositoryBase<Review>, IReviewRepository
{
    public ReviewRepository(ApplicationDbContext context)
        : base(context)
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
            return NotFound("review.getById.notFound", nameof(Review), id);
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
                "review.getByBookingId.notFound",
                $"Review for booking '{bookingId}' was not found.",
                bookingId));
        }

        return review;
    }

    public async Task<Result<IReadOnlyList<Review>, Failure>> GetByBarberProfileIdAsync(
        Guid barberProfileId,
        int page,
        int pageSize,
        bool onlyPublished = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(page, pageSize, "review.getByBarberProfileId") is { } pagingError)
        {
            return pagingError;
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
                "review.getPublishedPage.limit",
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

    public async Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var published = Context.Reviews
            .AsNoTracking()
            .Where(x => x.BarberProfileId == barberProfileId && x.IsPublished);

        var count = await published.CountAsync(cancellationToken);

        if (count == 0)
        {
            return new ReviewRatingSummary(null, 0);
        }

        // Округление до двух знаков делает домен (BarberProfile.UpdateRating),
        // репозиторий отдаёт среднее как есть.
        var average = await published.AverageAsync(x => (decimal)x.Rating, cancellationToken);

        return new ReviewRatingSummary(average, count);
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        await Context.Reviews.AddAsync(review, cancellationToken);

        // Второй отзыв на ту же бронь поймает уникальный индекс ux_reviews_booking_id.
        return await SaveChangesAsync("review.add", cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Review review,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(review, "review.update", cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        Context.Reviews.Remove(review);

        return await SaveChangesAsync("review.delete", cancellationToken);
    }
}
