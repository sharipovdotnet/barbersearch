using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews.GetMyReviews;

/// <summary>
/// Обработчик списка «мои отзывы» — отзывы текущего автора в любом статусе.
/// </summary>
/// <remarks>
/// Автор берётся из claim'а <c>sub</c>, а не из запроса: иначе клиент мог бы читать чужие
/// отзывы, подставив идентификатор. Скрытые и опубликованные отзывы идут вместе — кабинету
/// нужен один список, в котором статус лишь помечает строку. Порядок и keyset-условие задаёт
/// репозиторий (<c>created_at DESC, id</c>).
/// </remarks>
public sealed class GetMyReviewsHandler
    : IQueryHandler<PagedResponse<MyReviewResponse>, GetMyReviewsQuery>
{
    private readonly IReviewRepository _reviewRepository;
    private readonly ICurrentUser _currentUser;

    public GetMyReviewsHandler(IReviewRepository reviewRepository, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(reviewRepository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _reviewRepository = reviewRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает страницу отзывов автора с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<MyReviewResponse>, Failure>> Handle(
        GetMyReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Аноним до сюда не доходит: эндпоинт закрыт fallback-политикой. Проверка всё равно
        // остаётся здесь, чтобы читатель списка не мог стать анонимным при ослаблении политики.
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, "review.getMyReviews") is { } pageSizeError)
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

        var rowsResult = await _reviewRepository.GetMyPageAsync(
            userIdResult.Value,
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

        return new PagedResponse<MyReviewResponse>(items, nextCursor);
    }
}
