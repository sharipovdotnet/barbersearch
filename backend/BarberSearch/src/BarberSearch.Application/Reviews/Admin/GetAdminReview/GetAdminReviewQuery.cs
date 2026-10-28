using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Reviews.Admin;

namespace BarberSearch.Application.Reviews.Admin.GetAdminReview;

/// <summary>
/// Запрос одного отзыва для админской карточки.
/// </summary>
/// <remarks>
/// Нужен для прямых ссылок: отзыв может уже не попадать на первую страницу очереди,
/// а перезагрузка страницы должна возвращать ту же карточку.
/// </remarks>
/// <param name="ReviewId">Идентификатор отзыва.</param>
public record GetAdminReviewQuery(Guid ReviewId) : IQuery<AdminReviewResponse>;
