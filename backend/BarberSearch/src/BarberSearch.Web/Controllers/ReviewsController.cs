using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Reviews.CreateReview;
using BarberSearch.Application.Reviews.HideReview;
using BarberSearch.Application.Reviews.PublishReview;
using BarberSearch.Application.Reviews.UpdateReview;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.CreateReview;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Отзывы: создание по брони, правка, публикация и скрытие.
/// </summary>
/// <remarks>
/// Барбер выводится из брони, а не приходит от клиента, поэтому отзыв нельзя привязать
/// не к тому барберу. Пересчёт рейтинга барбера — отдельная операция его профиля.
/// </remarks>
[ApiController]
[Route("api/reviews")]
public class ReviewsController : ApiController
{
    /// <summary>
    /// Создаёт отзыв по завершённой брони. Повторный отзыв на ту же бронь — 409.
    /// </summary>
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
    /// Публикует отзыв: он начинает участвовать в рейтинге барбера.
    /// </summary>
    [HttpPost("{reviewId:guid}/publication")]
    public async Task<ActionResult> Publish(
        Guid reviewId,
        [FromServices] ICommandHandler<PublishReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new PublishReviewCommand(reviewId), cancellationToken));

    /// <summary>
    /// Скрывает отзыв: он перестаёт участвовать в рейтинге барбера.
    /// </summary>
    [HttpDelete("{reviewId:guid}/publication")]
    public async Task<ActionResult> Hide(
        Guid reviewId,
        [FromServices] ICommandHandler<HideReviewCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new HideReviewCommand(reviewId), cancellationToken));
}
