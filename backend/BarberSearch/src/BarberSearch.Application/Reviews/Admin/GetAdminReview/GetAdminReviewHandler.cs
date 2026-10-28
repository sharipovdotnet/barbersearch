using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Reviews.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews.Admin.GetAdminReview;

/// <summary>
/// Обработчик одного отзыва для админской карточки.
/// </summary>
public sealed class GetAdminReviewHandler
    : IQueryHandler<AdminReviewResponse, GetAdminReviewQuery>
{
    private readonly IReviewRepository _reviewRepository;

    public GetAdminReviewHandler(IReviewRepository reviewRepository)
    {
        ArgumentNullException.ThrowIfNull(reviewRepository);

        _reviewRepository = reviewRepository;
    }

    /// <summary>
    /// Возвращает отзыв любого статуса либо <c>404 review.notFound</c>.
    /// </summary>
    public async Task<Result<AdminReviewResponse, Failure>> Handle(
        GetAdminReviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ReviewId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                "admin.reviews.id",
                "Review id must not be empty.",
                nameof(query.ReviewId)));
        }

        return await _reviewRepository.GetAdminByIdAsync(query.ReviewId, cancellationToken);
    }
}
