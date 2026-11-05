using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberRequests.Admin.ApproveBarberRequest;
using BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequest;
using BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequests;
using BarberSearch.Application.BarberRequests.Admin.RejectBarberRequest;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.BarberRequests.RejectBarberRequest;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Администраторская работа с заявками в барберы: очередь, карточка, одобрение и отказ.
/// </summary>
/// <remarks>
/// <para>
/// Все действия закрыты политикой <c>AdminOnly</c>: в ответах есть PII заявителя (имя и email),
/// а решение меняет роль другого пользователя. Один контракт
/// <see cref="AdminBarberRequestResponse"/> используется и списком, и карточкой: набор полей
/// у них одинаковый, а второй «лёгкий» тип дал бы второй маппинг без выгоды.
/// </para>
/// <para>
/// <c>POST</c> — cookie-мутации: глобальный antiforgery-фильтр требует <c>X-CSRF-TOKEN</c>
/// для cookie-клиента, Bearer-клиент проходит без него. Одобрение меняет роль и ротирует
/// security stamp заявителя, поэтому его прежние сессии завершаются — это ожидаемо.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/barber-requests")]
public class AdminBarberRequestsController : ApiController
{
    /// <summary>
    /// Возвращает страницу заявок: очередь <c>pending</c> (по умолчанию) или все заявки.
    /// </summary>
    /// <remarks>
    /// <c>pending</c> — только <c>Pending</c>, старые сверху (FIFO); <c>all</c> — любой статус,
    /// свежие сверху. Keyset-пагинация, <c>pageSize</c> по умолчанию 20 и не больше 100;
    /// неизвестный статус, размер страницы или битый курсор — <c>400</c>.
    /// </remarks>
    /// <param name="status">Фильтр <c>pending</c> (по умолчанию) или <c>all</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminBarberRequestResponse>>> Get(
        [FromQuery] string? status,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<
            PagedResponse<AdminBarberRequestResponse>,
            GetAdminBarberRequestsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBarberRequestsQuery(status, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает одну заявку любого статуса для администраторской карточки.
    /// </summary>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{requestId:guid}")]
    public async Task<ActionResult<AdminBarberRequestResponse>> GetById(
        Guid requestId,
        [FromServices] IQueryHandler<
            AdminBarberRequestResponse,
            GetAdminBarberRequestQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBarberRequestQuery(requestId),
            cancellationToken));

    /// <summary>
    /// Одобряет заявку: пользователь получает роль <c>Barber</c>, его сессии завершаются.
    /// </summary>
    /// <remarks>
    /// Повторное решение — <c>409 barberRequest.review.invalidStatus</c>; неизвестная заявка —
    /// <c>404 barberRequest.notFound</c>. Профиль барбера не создаётся, <c>barberProfileId</c>
    /// остаётся <c>null</c> до онбординга.
    /// </remarks>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{requestId:guid}/approval")]
    public async Task<ActionResult<AdminBarberRequestResponse>> Approve(
        Guid requestId,
        [FromServices] ICommandHandler<
            AdminBarberRequestResponse,
            ApproveBarberRequestCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ApproveBarberRequestCommand(requestId),
            cancellationToken));

    /// <summary>
    /// Отклоняет заявку с обязательной причиной, не меняя роль заявителя.
    /// </summary>
    /// <remarks>
    /// Пустая причина — <c>400 barberRequest.rejection.reasonRequired</c>, длиннее 500 символов —
    /// <c>400 barberRequest.rejection.reasonTooLong</c>, повторное решение — <c>409
    /// barberRequest.review.invalidStatus</c>.
    /// </remarks>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="request">Причина отказа.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{requestId:guid}/rejection")]
    public async Task<ActionResult<AdminBarberRequestResponse>> Reject(
        Guid requestId,
        [FromBody] RejectBarberRequestRequest request,
        [FromServices] ICommandHandler<
            AdminBarberRequestResponse,
            RejectBarberRequestCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RejectBarberRequestCommand(requestId, request.Reason),
            cancellationToken));
}
