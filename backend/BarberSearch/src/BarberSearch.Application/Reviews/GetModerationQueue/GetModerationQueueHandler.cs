using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews.GetModerationQueue;

/// <summary>
/// Обработчик очереди модерации — скрытые отзывы всех барберов, старые сверху.
/// </summary>
/// <remarks>
/// Только административный сценарий; роль проверяет контроллер политикой <c>AdminOnly</c>.
/// Порядок задаёт репозиторий (<c>created_at ASC, id</c>), курсор хранит пару
/// <c>(created_at, id)</c>, поэтому декодирование общее с карточкой барбера
/// (<see cref="ReviewCursor"/>), а направление меняет только запрос.
/// </remarks>
public sealed class GetModerationQueueHandler
    : IQueryHandler<PagedResponse<ReviewResponse>, GetModerationQueueQuery>
{
    private readonly IReviewRepository _reviewRepository;

    public GetModerationQueueHandler(IReviewRepository reviewRepository)
    {
        ArgumentNullException.ThrowIfNull(reviewRepository);

        _reviewRepository = reviewRepository;
    }

    /// <summary>
    /// Возвращает страницу скрытых отзывов с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<ReviewResponse>, Failure>> Handle(
        GetModerationQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        // Свой префикс кода: админский экран отличается от публичного списка барбера.
        if (Pagination.ValidatePageSize(pageSize, "review.moderation") is { } pageSizeError)
        {
            return pageSizeError;
        }

        ReviewCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = ReviewCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _reviewRepository.GetModerationPageAsync(
            cursor,
            pageSize + 1,
            cancellationToken);
        if (rowsResult.IsFailure)
        {
            return rowsResult.Error;
        }

        var rows = rowsResult.Value;
        var hasNextPage = rows.Count > pageSize;
        var items = hasNextPage
            ? rows.Take(pageSize).ToList()
            : rows.ToList();

        var nextCursor = hasNextPage && items.Count > 0
            ? new ReviewCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<ReviewResponse>(items, nextCursor);
    }
}
