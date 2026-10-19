using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IReviewRepository"/> для тестов обработчиков. Повторный отзыв
/// на ту же бронь отклоняется так же, как это делает уникальный индекс в БД.
/// </summary>
public sealed class FakeReviewRepository : IReviewRepository
{
    private readonly Dictionary<Guid, Review> _reviews = [];

    public IReadOnlyCollection<Review> Reviews => _reviews.Values;

    public void Add(Review review) => _reviews[review.Id] = review;

    public Task<Result<Review, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<Review, Failure>>(
            _reviews.TryGetValue(id, out var review)
                ? review
                : new Failure(Error.NotFound(
                    "review.getById.notFound",
                    $"Review '{id}' was not found.",
                    id)));

    public Task<Result<Review, Failure>> GetByBookingIdAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var review = _reviews.Values.FirstOrDefault(x => x.BookingId == bookingId);

        if (review is null)
        {
            return Task.FromResult<Result<Review, Failure>>(new Failure(Error.NotFound(
                "review.getByBookingId.notFound",
                $"Review of booking '{bookingId}' was not found.",
                bookingId)));
        }

        return Task.FromResult<Result<Review, Failure>>(review);
    }

    public Task<Result<IReadOnlyList<Review>, Failure>> GetByBarberProfileIdAsync(
        Guid barberProfileId,
        int page,
        int pageSize,
        bool onlyPublished = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetPublishedPageAsync(
        Guid barberProfileId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Review> query = _reviews.Values
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt < position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<ReviewResponse> rows = query
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<ReviewResponse>, Failure>(rows));
    }

    public Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<UnitResult<Failure>> AddAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        if (_reviews.Values.Any(x => x.BookingId == review.BookingId))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                "review.add.duplicate",
                "A review for this booking already exists."))));
        }

        _reviews[review.Id] = review;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        Review review,
        CancellationToken cancellationToken = default)
        => Task.FromResult(UnitResult.Success<Failure>());

    public Task<UnitResult<Failure>> DeleteAsync(
        Review review,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static ReviewResponse ToResponse(Review review)
        => new(
            review.Id,
            review.BarberProfileId,
            review.UserId,
            review.BookingId,
            review.Rating,
            review.Text,
            review.IsPublished,
            review.CreatedAt,
            review.UpdatedAt);
}
