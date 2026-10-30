using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;

namespace BarberSearch.Application.Reviews.GetMyReviews;

/// <summary>
/// Запрос списка «мои отзывы»: отзывы текущего автора в любом статусе.
/// </summary>
/// <remarks>
/// Параметра <c>userId</c> нет намеренно: список всегда принадлежит вызывающему, поэтому
/// администратор видит здесь только свои отзывы, а для модерации у него есть
/// <c>api/admin/reviews</c>. Порядок — свежие сверху, пагинация курсорная.
/// </remarks>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetMyReviewsQuery(
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<MyReviewResponse>>;
