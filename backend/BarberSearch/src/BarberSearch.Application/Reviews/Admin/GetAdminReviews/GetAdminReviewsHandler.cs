using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews.Admin.GetAdminReviews;

/// <summary>
/// Обработчик админского списка отзывов.
/// </summary>
/// <remarks>
/// Очередь модерации отдаётся в порядке <c>updated_at ASC, id</c> — по моменту попадания
/// в очередь, поэтому отредактированный опубликованный отзыв встаёт в конец, а не в начало,
/// где «ждал» бы с даты создания. Опубликованные — в порядке <c>created_at DESC, id</c>.
/// Курсор у вкладок разный: очередь хранит <see cref="ModerationCursor"/>, опубликованные —
/// <see cref="ReviewCursor"/>. Доступ проверяет контроллер политикой <c>AdminOnly</c>.
/// </remarks>
public sealed class GetAdminReviewsHandler
    : IQueryHandler<PagedResponse<AdminReviewResponse>, GetAdminReviewsQuery>
{
    private const string STATUS_ERROR_CODE = "admin.reviews.status";
    private const string PAGE_SIZE_ERROR_PREFIX = "admin.reviews";

    private readonly IReviewRepository _reviewRepository;

    public GetAdminReviewsHandler(IReviewRepository reviewRepository)
    {
        ArgumentNullException.ThrowIfNull(reviewRepository);

        _reviewRepository = reviewRepository;
    }

    /// <summary>
    /// Возвращает страницу отзывов выбранного статуса с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminReviewResponse>, Failure>> Handle(
        GetAdminReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var isPublished = ParseStatus(query.Status);

        if (isPublished is null)
        {
            return new Failure(Error.Validation(
                STATUS_ERROR_CODE,
                "Status must be either 'pending' or 'published'.",
                nameof(query.Status)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, PAGE_SIZE_ERROR_PREFIX) is { } pageSizeError)
        {
            return pageSizeError;
        }

        return isPublished.Value
            ? await ReadPublishedAsync(query.Cursor, pageSize, cancellationToken)
            : await ReadModerationAsync(query.Cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Очередь модерации: курсор — пара <c>(updated_at, id)</c>.
    /// </summary>
    private async Task<Result<PagedResponse<AdminReviewResponse>, Failure>> ReadModerationAsync(
        string? rawCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ModerationCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(rawCursor))
        {
            var decoded = ModerationCursor.Decode(rawCursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _reviewRepository.GetAdminModerationPageAsync(
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
            ? new ModerationCursor(items[^1].UpdatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminReviewResponse>(items, nextCursor);
    }

    /// <summary>
    /// Опубликованные: курсор — пара <c>(created_at, id)</c>.
    /// </summary>
    private async Task<Result<PagedResponse<AdminReviewResponse>, Failure>> ReadPublishedAsync(
        string? rawCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ReviewCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(rawCursor))
        {
            var decoded = ReviewCursor.Decode(rawCursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _reviewRepository.GetAdminPublishedPageAsync(
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

        return new PagedResponse<AdminReviewResponse>(items, nextCursor);
    }

    private static bool? ParseStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "pending" => false,
            "published" => true,
            _ => null
        };
}
