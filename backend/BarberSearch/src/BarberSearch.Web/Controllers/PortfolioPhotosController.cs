using BarberSearch.Application.Abstractions;
using BarberSearch.Application.PortfolioPhotos.PublishPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UnpublishPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Contracts.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Фотография портфолио: публикация и подпись.
/// </summary>
/// <remarks>
/// Сама фотография — дочерняя сущность агрегата <c>BarberProfile</c>, поэтому адрес идёт
/// через профиль. Создание, удаление и изменение порядка фотографии живут в
/// <see cref="BarberProfilesController"/> (это методы агрегата), здесь — операции самой
/// фотографии. Порядок вывода меняется только через агрегат:
/// <c>PUT api/barber-profiles/{barberProfileId}/photos/{photoId}/sort-order</c> (R-44).
/// </remarks>
[ApiController]
[Route("api/barber-profiles/{barberProfileId:guid}/photos")]
public class PortfolioPhotosController : ApiController
{
    /// <summary>
    /// Публикует фотографию портфолио.
    /// </summary>
    [HttpPost("{photoId:guid}/publication")]
    public async Task<ActionResult> Publish(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<PublishPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new PublishPortfolioPhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Снимает фотографию портфолио с публикации.
    /// </summary>
    [HttpDelete("{photoId:guid}/publication")]
    public async Task<ActionResult> Unpublish(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<UnpublishPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnpublishPortfolioPhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Меняет подпись к фотографии. <c>null</c> в теле убирает подпись.
    /// </summary>
    [HttpPut("{photoId:guid}/description")]
    public async Task<ActionResult> UpdateDescription(
        Guid barberProfileId,
        Guid photoId,
        [FromBody] UpdatePortfolioPhotoDescriptionRequest request,
        [FromServices] ICommandHandler<UpdatePortfolioPhotoDescriptionCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdatePortfolioPhotoDescriptionCommand(barberProfileId, photoId, request.Description),
            cancellationToken));
}
