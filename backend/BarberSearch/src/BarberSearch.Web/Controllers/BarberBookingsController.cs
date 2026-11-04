using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.Bookings.GetBarberBookings;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Записи барбера: чтение собственных броней из кабинета.
/// </summary>
/// <remarks>
/// Маршрут отдельный от <c>api/bookings</c>, потому что тот отдаёт брони, где текущий
/// пользователь — клиент. Профиль барбера берётся из claim'а сессии, поэтому параметра
/// с идентификатором профиля нет: барбер не может прочитать чужие записи.
/// <para>
/// Политика <c>BarberOnly</c> отсекает анонима (<c>401</c>) и роли <c>Customer</c>/<c>Admin</c>
/// (<c>403</c>). Отдельной rate-limit политики нет: <c>GET</c> глобальным лимитером
/// не ограничивается.
/// </para>
/// </remarks>
[ApiController]
[Route("api/barber/bookings")]
[Authorize(Policy = AuthorizationPolicies.BarberOnly)]
public class BarberBookingsController : ApiController
{
    /// <summary>
    /// Возвращает записи текущего барбера: ближайшие или историю.
    /// </summary>
    /// <remarks>
    /// Скоуп по умолчанию — <c>upcoming</c>: активные брони, у которых визит ещё не закончился,
    /// по возрастанию времени. <c>past</c> — всё остальное, свежие сверху. Порядок задаёт сервер,
    /// пагинация курсорная, направление курсора зависит от скоупа.
    /// </remarks>
    /// <param name="scope">Скоуп: <c>upcoming</c> (по умолчанию) или <c>past</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BarberBookingResponse>>> Get(
        [FromQuery] string? scope,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<BarberBookingResponse>, GetBarberBookingsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberBookingsQuery(scope, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Записывает клиента к текущему барберу; бронь создаётся сразу подтверждённой.
    /// </summary>
    /// <remarks>
    /// Профиль барбера берётся из claim'а <c>barber_profile_id</c>, а не из тела:
    /// записать клиента к чужому мастеру нельзя. Клиент должен быть зарегистрированным
    /// и активным; его идентификатор приходит из формы поиска <c>GET api/barber/clients</c>.
    /// Цена берётся из услуги барбера, как и в публичной записи. Это cookie-мутация,
    /// поэтому глобальный antiforgery-фильтр требует <c>X-CSRF-TOKEN</c>.
    /// </remarks>
    /// <param name="request">Клиент, услуга, салон и время визита.</param>
    /// <param name="currentUser">Текущая сессия: профиль барбера из claim'а.</param>
    /// <param name="handler">Обработчик создания брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateBarberBookingRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] ICommandHandler<Guid, CreateBookingCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetBarberProfileId(out var barberProfileId))
        {
            return Forbid();
        }

        var createRequest = new CreateBookingRequest(
            barberProfileId,
            request.BarberServiceId,
            request.SalonId,
            request.StartAt,
            request.EndAt,
            request.IdempotencyKey,
            request.CustomerNote);

        return ToCreatedResult(await handler.Handle(
            new CreateBookingCommand(
                createRequest,
                request.CustomerId,
                ConfirmImmediately: true),
            cancellationToken));
    }
}
