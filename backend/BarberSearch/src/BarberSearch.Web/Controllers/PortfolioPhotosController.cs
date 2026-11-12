using BarberSearch.Application.Abstractions;
using BarberSearch.Application.PortfolioPhotos.ApprovePortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.BlockPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.PublishPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.RejectPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UnblockPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UnpublishPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Contracts.Photos;
using BarberSearch.Contracts.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
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

    /// <summary>
    /// Блокирует фотографию портфолио: кадр исчезает из публичной карточки и перестаёт
    /// раздаваться по URL. Только администратор.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Блокировка — отдельное состояние от <c>IsPublished</c>: владелец снимает и возвращает
    /// публикацию сам (<c>POST/DELETE .../publication</c>), а снять административную блокировку
    /// не может. Поэтому действие закрыто политикой <c>AdminOnly</c>, а обработчик независимо
    /// от неё проверяет роль через <c>EnsureAdmin</c>: команду можно вызвать и напрямую.
    /// </para>
    /// <para>
    /// Ответ <c>204</c>, повторная блокировка идемпотентна. Фотографии нет в портфолио
    /// профиля — <c>404 portfolioPhoto.block.notFound</c>.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Блокируемая фотография.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{photoId:guid}/block")]
    public async Task<ActionResult> Block(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<BlockPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new BlockPortfolioPhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Снимает административную блокировку фотографии. Только администратор; повторный вызов
    /// идемпотентен.
    /// </summary>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Разблокируемая фотография.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{photoId:guid}/block")]
    public async Task<ActionResult> Unblock(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<UnblockPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnblockPortfolioPhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Одобряет фотографию портфолио, ждущую проверки. Только администратор.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Одобрение — допуск к публикации, а не публикация: кадр появится публично, только если
    /// владелец не снял его с публикации (<c>isPublished</c>) и он не заблокирован
    /// администратором. Повторное решение по проверенному кадру — не <c>204</c>: домен
    /// возвращает <c>409 portfolioPhoto.moderation.invalidStatus</c>, и интерфейс перечитывает
    /// очередь.
    /// </para>
    /// <para>
    /// Ответ <c>204</c>. Профиля нет — <c>404 barberProfile.notFound</c>, фотографии нет в этом
    /// профиле — <c>404 portfolioPhoto.moderation.notFound</c>. Решение пишется в поля самой
    /// сущности (<c>reviewedByUserId</c>, <c>reviewedAt</c>), а строка
    /// <c>portfolio_photo.updated</c> фиксирует смену <c>moderationStatus</c>.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Одобряемая фотография.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{photoId:guid}/approval")]
    public async Task<ActionResult> Approve(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<ApprovePortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ApprovePortfolioPhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Отклоняет фотографию портфолио с обязательной причиной. Только администратор.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Причина — текст администратора для владельца профиля: домен тримит её, требует 1..500
    /// символов и возвращает <c>400 portfolioPhoto.moderation.reasonRequired</c> /
    /// <c>.reasonTooLong</c> с <c>invalidField = "reason"</c>. В <c>audit_log</c> причина
    /// не попадает: строка <c>portfolio_photo.updated</c> хранит только <c>moderationStatus</c>.
    /// </para>
    /// <para>
    /// Ответ <c>204</c>. Профиля нет — <c>404 barberProfile.notFound</c>, фотографии нет —
    /// <c>404 portfolioPhoto.moderation.notFound</c>, кадр уже не <c>Pending</c> — <c>409
    /// portfolioPhoto.moderation.invalidStatus</c>.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Отклоняемая фотография.</param>
    /// <param name="request">Тело с причиной отказа.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{photoId:guid}/rejection")]
    public async Task<ActionResult> Reject(
        Guid barberProfileId,
        Guid photoId,
        [FromBody] RejectPhotoRequest request,
        [FromServices] ICommandHandler<RejectPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RejectPortfolioPhotoCommand(barberProfileId, photoId, request.Reason),
            cancellationToken));
}
