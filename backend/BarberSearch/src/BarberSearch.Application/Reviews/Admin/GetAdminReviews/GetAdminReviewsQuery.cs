using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;

namespace BarberSearch.Application.Reviews.Admin.GetAdminReviews;

/// <summary>
/// Запрос админского списка отзывов: очередь модерации или опубликованные.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>pending</c> (по умолчанию) или <c>published</c>.
/// Неизвестное значение отклоняет обработчик, а не model binding: клиенту нужен
/// публичный код ошибки, а не общий <c>ValidationProblemDetails</c> от биндера.
/// </remarks>
/// <param name="Status">Фильтр: <c>pending</c> или <c>published</c>.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetAdminReviewsQuery(
    string? Status,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminReviewResponse>>;
