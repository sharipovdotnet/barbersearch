using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews.GetReviews;

/// <summary>
/// Обработчик списка опубликованных отзывов барбера.
/// </summary>
/// <remarks>
/// В выдачу попадают только опубликованные отзывы. Репозиторий проецирует их сразу
/// в <see cref="ReviewResponse"/> без навигаций, поэтому циклический граф
/// «отзыв → бронь → барбер → отзывы» наружу не выходит.
/// <para>
/// <see cref="GetReviewsQuery.BarberProfileId"/> со значением <see cref="Guid.Empty"/>
/// отклоняется здесь с кодом <c>review.getReviews.barberProfileId</c>. Это защита в глубину:
/// отсутствие параметра в HTTP-запросе отсекает model binding контроллера
/// (<c>[BindRequired]</c>, 400 на поле <c>barberProfileId</c>), а явный пустой GUID
/// по-прежнему получает доменную validation-ошибку.
/// </para>
/// </remarks>
public sealed class GetReviewsHandler
    : IQueryHandler<PagedResponse<ReviewResponse>, GetReviewsQuery>
{
    private readonly IReviewRepository _reviewRepository;

    public GetReviewsHandler(IReviewRepository reviewRepository)
    {
        ArgumentNullException.ThrowIfNull(reviewRepository);

        _reviewRepository = reviewRepository;
    }

    /// <summary>
    /// Возвращает страницу опубликованных отзывов с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<ReviewResponse>, Failure>> Handle(
        GetReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.BarberProfileId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                "review.getReviews.barberProfileId",
                DomainMessages.BARBER_PROFILE_ID_REQUIRED,
                nameof(query.BarberProfileId)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, "review.getReviews") is { } pageSizeError)
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

        var rowsResult = await _reviewRepository.GetPublishedPageAsync(
            query.BarberProfileId,
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
