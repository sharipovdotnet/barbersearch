using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Reviews.Admin.GetAdminReview;
using BarberSearch.Application.Reviews.Admin.GetAdminReviews;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админское чтение отзывов: очередь модерации, опубликованные и карточка по идентификатору.
/// </summary>
/// <remarks>
/// Отдельный контроллер, потому что контракт отличается от публичного: в ответ входят имя
/// барбера, клиент и контекст визита. Все действия закрыты политикой <c>AdminOnly</c>;
/// публичные <c>GET api/reviews</c> и <c>GET api/reviews/moderation</c> не меняются.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/reviews")]
public class AdminReviewsController : ApiController
{
    /// <summary>
    /// Возвращает страницу отзывов: скрытые (FIFO) или опубликованные (свежие сверху).
    /// </summary>
    /// <param name="status">Фильтр <c>pending</c> (по умолчанию) или <c>published</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminReviewResponse>>> Get(
        [FromQuery] string? status,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<AdminReviewResponse>, GetAdminReviewsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminReviewsQuery(status, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает один отзыв любого статуса для админской карточки.
    /// </summary>
    /// <param name="reviewId">Идентификатор отзыва.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{reviewId:guid}")]
    public async Task<ActionResult<AdminReviewResponse>> GetById(
        Guid reviewId,
        [FromServices] IQueryHandler<AdminReviewResponse, GetAdminReviewQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminReviewQuery(reviewId),
            cancellationToken));
}
