using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Reviews.CreateReview;
using BarberSearch.Application.Reviews.DeleteReview;
using BarberSearch.Application.Reviews.GetModerationQueue;
using BarberSearch.Application.Reviews.GetMyReviews;
using BarberSearch.Application.Reviews.GetReviews;
using BarberSearch.Application.Reviews.HideReview;
using BarberSearch.Application.Reviews.PublishReview;
using BarberSearch.Application.Reviews.UpdateReview;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.CreateReview;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Отзывы: публичный список, свой список автора, создание по брони, правка, удаление,
/// публикация, скрытие и очередь модерации.
/// </summary>
/// <remarks>
/// <c>GET api/reviews</c> открыт анонимно: список опубликованных отзывов — часть карточки
/// барбера для витрины. Лимит — политика <c>reads</c> (120 запросов в минуту на адрес
/// клиента). Создание и правка отзыва остаются закрытыми fallback-политикой; с R-35
/// создание оставляет отзыв на модерации (<c>IsPublished = false</c>), а публикует и скрывает
/// его только администратор. Очередь скрытых отзывов всех барберов —
/// <c>GET api/reviews/moderation</c>, тоже только администратор. В ответе только
/// идентификаторы и текст отзыва: ни вложенных данных автора или брони, ни контактных данных.
/// <para>
/// Чего нет в публичной выдаче, автор видит в своём списке: <c>GET api/reviews/mine</c> отдаёт
/// отзывы текущего пользователя в обоих статусах и с контекстом визита, а
/// <c>DELETE api/reviews/{id}</c> удаляет свой отзыв и пересчитывает рейтинг, если тот был
/// опубликован.
/// </para>
/// <para>
/// Барбер выводится из брони, а не приходит от клиента, поэтому отзыв нельзя привязать
/// не к тому барберу. Рейтинг барбера меняется при публикации, скрытии и правке
/// опубликованного отзыва — в одной транзакции с изменением отзыва (<c>IUnitOfWork</c>);
/// создание скрытого отзыва публичную сводку не трогает.
/// </para>
/// </remarks>
[ApiController]
[Route("api/reviews")]
public class ReviewsController : ApiController
{
    /// <summary>
    /// Возвращает опубликованные отзывы барбера.
    /// </summary>
    /// <remarks>
    /// Анонимное действие с лимитом <c>reads</c> 120/мин на IP. Скрытые отзывы не попадают
    /// в выдачу даже администратору: для модерации есть очередь <c>GET api/reviews/moderation</c>
    /// и отдельные действия публикации и скрытия. Порядок — свежие сверху, пагинация курсорная.
    /// <para>
    /// <c>barberProfileId</c> обязателен, и отсутствие параметра отсекает model binding:
    /// <c>[BindRequired]</c> даёт <c>400 ValidationProblemDetails</c> с ошибкой на поле
    /// <c>barberProfileId</c> до вызова обработчика. Явный пустой GUID
    /// (<c>00000000-0000-0000-0000-000000000000</c>) по-прежнему отклоняет проверка в
    /// <c>GetReviewsHandler</c> с кодом <c>review.getReviews.barberProfileId</c> —
    /// defense-in-depth, а не замена binding'а.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера; обязателен.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ReviewResponse>>> Get(
        [FromQuery, BindRequired] Guid barberProfileId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<ReviewResponse>, GetReviewsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetReviewsQuery(barberProfileId, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Очередь модерации: скрытые отзывы всех барберов, старые сверху. Только администратор.
    /// </summary>
    /// <remarks>
    /// Keyset-пагинация, порядок <c>created_at ASC, id</c> (FIFO), курсор общий с публичным
    /// списком. Отзыв, созданный клиентом, сразу попадает сюда: с R-35 создание оставляет его
    /// скрытым. Опубликованные отзывы в очередь не входят.
    /// </remarks>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("moderation")]
    public async Task<ActionResult<PagedResponse<ReviewResponse>>> GetModerationQueue(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<ReviewResponse>, GetModerationQueueQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetModerationQueueQuery(cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Свои отзывы автора в любом статусе — для кабинета «Мои отзывы».
    /// </summary>
    /// <remarks>
    /// Действие закрыто fallback-политикой: аноним получает <c>401</c>. Автор берётся из
    /// claim'а <c>sub</c>, параметра <c>userId</c> нет — администратор тоже видит здесь только
    /// свои отзывы, а для модерации есть <c>GET api/admin/reviews</c>. Возвращаются и скрытые,
    /// и опубликованные отзывы: парная пре-модерация держит новый отзыв скрытым, поэтому до
    /// публикации автор видит его только здесь. <c>isPublished = false</c> означает «на
    /// модерации» (в том числе скрыт администратором — бэкенд не различает эти случаи).
    /// <para>
    /// Порядок — свежие сверху, пагинация курсорная. Отдельная политика <c>reads</c> не
    /// добавляется: аутентифицированные чтения живут под общим лимитом 30/мин.
    /// </para>
    /// </remarks>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("mine")]
    public async Task<ActionResult<PagedResponse<MyReviewResponse>>> GetMine(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<MyReviewResponse>, GetMyReviewsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetMyReviewsQuery(cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Удаляет свой отзыв. Опубликованный отзыв пересчитывает рейтинг в той же транзакции.
    /// </summary>
    /// <remarks>
    /// Удалить может только автор: чужой отзыв — <c>403 auth.forbidden</c>, неизвестный или
    /// уже удалённый — <c>404 review.notFound</c>. Администратор этим действием отзыв не
    /// удаляет: у него есть скрытие <c>DELETE api/reviews/{id}/publication</c>. Удаление
    /// жёсткое и освобождает уникальный индекс по брони, поэтому автор снова может оставить
    /// отзыв по этому визиту.
    /// </remarks>
    /// <param name="reviewId">Идентификатор отзыва.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpDelete("{reviewId:guid}")]
    public async Task<ActionResult> Delete(
        Guid reviewId,
        [FromServices] ICommandHandler<DeleteReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new DeleteReviewCommand(reviewId), cancellationToken));

    /// <summary>
    /// Создаёт отзыв по завершённой брони. Отзыв скрыт до публикации администратором.
    /// Повторный отзыв на ту же бронь — 409.
    /// </summary>
    /// <remarks>
    /// Действие остаётся закрытым (fallback-политика): аноним получает 401, а автор
    /// определяется по текущему пользователю через бронь, а не приходит в теле.
    /// С R-35 созданный отзыв не влияет на рейтинг барбера, пока администратор не вызовет
    /// <c>POST api/reviews/{id}/publication</c>.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateReviewRequest request,
        [FromServices] ICommandHandler<Guid, CreateReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(
            new CreateReviewCommand(request.BookingId, request.Rating, request.Text),
            cancellationToken));

    /// <summary>
    /// Меняет оценку и текст отзыва.
    /// </summary>
    [HttpPut("{reviewId:guid}")]
    public async Task<ActionResult> Update(
        Guid reviewId,
        [FromBody] UpdateReviewRequest request,
        [FromServices] ICommandHandler<UpdateReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateReviewCommand(reviewId, request.Rating, request.Text),
            cancellationToken));

    /// <summary>
    /// Публикует отзыв: он начинает участвовать в рейтинге барбера. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{reviewId:guid}/publication")]
    public async Task<ActionResult> Publish(
        Guid reviewId,
        [FromServices] ICommandHandler<PublishReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new PublishReviewCommand(reviewId), cancellationToken));

    /// <summary>
    /// Скрывает отзыв: он перестаёт участвовать в рейтинге барбера. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{reviewId:guid}/publication")]
    public async Task<ActionResult> Hide(
        Guid reviewId,
        [FromServices] ICommandHandler<HideReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new HideReviewCommand(reviewId), cancellationToken));
}
