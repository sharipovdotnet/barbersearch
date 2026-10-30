using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Shared;
using System.Reflection;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IReviewRepository"/> для тестов обработчиков. Повторный отзыв
/// на ту же бронь отклоняется так же, как это делает уникальный индекс в БД.
/// </summary>
public sealed class FakeReviewRepository : IReviewRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, Review> _reviews = [];
    private readonly List<AdminReviewResponse> _adminReviews = [];
    private Dictionary<Guid, Review>? _snapshot;

    public IReadOnlyCollection<Review> Reviews => _reviews.Values;

    public void Add(Review review) => _reviews[review.Id] = review;

    public void AddAdminReview(AdminReviewResponse review) => _adminReviews.Add(review);

    public void BeginTransaction()
        => _snapshot = _reviews.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _reviews.Clear();

        foreach (var entry in _snapshot)
        {
            _reviews[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    public Task<Result<Review, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<Review, Failure>>(
            _reviews.TryGetValue(id, out var review)
                ? review
                : new Failure(Error.NotFound(
                    ApiErrorCodes.Review.NotFound,
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
                ApiErrorCodes.Review.NotFound,
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

    public Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetModerationPageAsync(
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Review> query = _reviews.Values.Where(review => !review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt > position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<ReviewResponse> rows = query
            .OrderBy(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<ReviewResponse>, Failure>(rows));
    }

    /// <summary>
    /// Страница «мои отзывы»: фильтр по автору, оба статуса, порядок и keyset как в БД.
    /// </summary>
    /// <remarks>
    /// Контекст визита фейк не моделирует — у <see cref="Review"/> его нет; он подставляется
    /// заглушкой. Реальные имена барбера, услуги и салона проверяет интеграционный тест.
    /// </remarks>
    public Task<Result<IReadOnlyList<MyReviewResponse>, Failure>> GetMyPageAsync(
        Guid userId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<MyReviewResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.Review.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        IEnumerable<Review> query = _reviews.Values
            .Where(review => review.UserId == userId);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt < position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<MyReviewResponse> rows = query
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(ToMyResponse)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<MyReviewResponse>, Failure>(rows));
    }

    public Task<Result<IReadOnlyList<AdminReviewResponse>, Failure>> GetAdminPageAsync(
        bool isPublished,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Task.FromResult<Result<IReadOnlyList<AdminReviewResponse>, Failure>>(
                new Failure(Error.Validation(
                    ApiErrorCodes.Review.PageLimitInvalid,
                    "Page limit must be positive.",
                    nameof(limit))));
        }

        IEnumerable<AdminReviewResponse> query = _adminReviews
            .Where(review => review.IsPublished == isPublished);

        if (cursor is { } position)
        {
            query = isPublished
                ? query.Where(review =>
                    review.CreatedAt < position.CreatedAt
                    || (review.CreatedAt == position.CreatedAt
                        && review.Id.CompareTo(position.Id) > 0))
                : query.Where(review =>
                    review.CreatedAt > position.CreatedAt
                    || (review.CreatedAt == position.CreatedAt
                        && review.Id.CompareTo(position.Id) > 0));
        }

        IReadOnlyList<AdminReviewResponse> rows = (isPublished
                ? query.OrderByDescending(review => review.CreatedAt)
                : query.OrderBy(review => review.CreatedAt))
            .ThenBy(review => review.Id)
            .Take(limit)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<AdminReviewResponse>, Failure>(rows));
    }

    public Task<Result<AdminReviewResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var review = _adminReviews.FirstOrDefault(candidate => candidate.Id == id);

        if (review is null)
        {
            return Task.FromResult<Result<AdminReviewResponse, Failure>>(
                new Failure(Error.NotFound(
                    ApiErrorCodes.Review.NotFound,
                    $"Review '{id}' was not found.",
                    id)));
        }

        return Task.FromResult(Result.Success<AdminReviewResponse, Failure>(review));
    }

    public Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var published = _reviews.Values
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished)
            .ToList();

        if (published.Count == 0)
        {
            return Task.FromResult(
                Result.Success<ReviewRatingSummary, Failure>(new ReviewRatingSummary(null, 0)));
        }

        var average = published.Average(review => (decimal)review.Rating);

        return Task.FromResult(
            Result.Success<ReviewRatingSummary, Failure>(
                new ReviewRatingSummary(average, published.Count)));
    }

    public Task<UnitResult<Failure>> AddAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        if (_reviews.Values.Any(x => x.BookingId == review.BookingId))
        {
            return Task.FromResult(UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                ApiErrorCodes.Review.AddUniqueViolation,
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
    {
        _reviews.Remove(review.Id);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    private static Review Clone(Review review)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (Review)memberwiseClone.Invoke(review, null)!;
    }

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

    private static MyReviewResponse ToMyResponse(Review review)
        => new(
            review.Id,
            review.Rating,
            review.Text,
            review.IsPublished,
            review.CreatedAt,
            review.UpdatedAt,
            new MyReviewBarberResponse(review.BarberProfileId, "Fake Barber"),
            new MyReviewVisitResponse(
                review.BookingId,
                review.CreatedAt,
                "UTC",
                0m,
                "Fake service",
                "Fake salon"));
}
