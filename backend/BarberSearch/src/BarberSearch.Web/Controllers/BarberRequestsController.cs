using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberRequests.CreateBarberRequest;
using BarberSearch.Application.BarberRequests.GetMyBarberRequest;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Заявка клиента на роль барбера: подача и чтение своей последней заявки.
/// </summary>
/// <remarks>
/// <para>
/// Оба действия закрыты fallback-политикой аутентификации: аноним получает <c>401</c>.
/// Роль в теле не принимается — заявка не выдаёт права, а <c>Barber</c> появляется только
/// после одобрения администратором. Подать заявку может только <c>Customer</c>; барбер и
/// администратор получают <c>403 barberRequest.create.forbidden</c>.
/// </para>
/// <para>
/// <c>POST</c> — cookie-мутация, поэтому глобальный antiforgery-фильтр требует пару
/// <c>X-CSRF-TOKEN</c> + cookie; Bearer-клиент проходит без неё. Ответ на создание — <c>201</c>
/// с самой заявкой, а не с одним идентификатором: заявитель сразу видит статус и дату подачи,
/// а отдельного чтения «своей заявки по id» нет.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/barber-requests")]
public class BarberRequestsController : ApiController
{
    /// <summary>
    /// Создаёт заявку от текущего пользователя в статусе <c>Pending</c>.
    /// </summary>
    /// <remarks>
    /// Повторная активная заявка — <c>409 barberRequest.create.alreadyPending</c>, в том числе
    /// при гонке двух одновременных подач. Ошибки полей приходят стандартным
    /// <c>ValidationProblemDetails</c>.
    /// </remarks>
    /// <param name="request">Данные заявки.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost]
    public async Task<ActionResult<BarberRequestResponse>> Create(
        [FromBody] CreateBarberRequestRequest request,
        [FromServices] ICommandHandler<BarberRequestResponse, CreateBarberRequestCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new CreateBarberRequestCommand(request),
            cancellationToken);

        // 201 с телом заявки: Location не выставляется, потому что чтения по id у заявителя нет
        // (R-42). Клиент продолжает работать с только что полученным объектом.
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : FailureProblemDetails.CreateResult(result.Error);
    }

    /// <summary>
    /// Возвращает последнюю заявку текущего пользователя или <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Доступно любой роли: после одобрения человек уже барбер, но должен видеть статус.
    /// Заявки может не быть — это не <c>404</c>, а <c>200 { request: null }</c>.
    /// </remarks>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("mine")]
    public async Task<ActionResult<MyBarberRequestResponse>> GetMine(
        [FromServices] IQueryHandler<MyBarberRequestResponse, GetMyBarberRequestQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetMyBarberRequestQuery(),
            cancellationToken));
}
