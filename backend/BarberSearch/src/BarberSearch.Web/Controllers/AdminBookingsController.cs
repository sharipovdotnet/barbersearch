using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings.Admin.GetAdminBooking;
using BarberSearch.Application.Bookings.Admin.GetAdminBookings;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админское чтение броней: список поддержки с поиском и карточка визита.
/// </summary>
/// <remarks>
/// <para>
/// Читает только администратор: политика <c>AdminOnly</c> закрывает оба действия, аноним
/// получает <c>401</c>, клиент и барбер — <c>403</c>. Ответ несёт email и телефон клиента —
/// это PII, и она допустима только здесь, в админском контракте; публичные чтения броней
/// (<c>GET api/bookings</c>, <c>GET api/barber/bookings</c>) этих полей не содержат.
/// </para>
/// <para>
/// Мутаций здесь нет и не появится: отмена и отметка неявки остаются на
/// <c>BookingsController</c> (<c>POST api/bookings/{id}/cancellation</c> и
/// <c>POST api/bookings/{id}/no-show</c>) — этап добавления операций не делал.
/// </para>
/// <para>
/// Список читается свежими сверху (<c>start_at DESC, id ASC</c>): поддержке нужен последний
/// визит клиента, а не начало его истории. Второй ключ (<c>id</c>) обязателен, потому что
/// у разных барберов визиты могут начинаться в одну минуту. Пагинация — keyset по
/// <c>(start_at, id)</c>: у неё нет OFFSET, поэтому глубокие страницы не деградируют,
/// а непрозрачный курсор не даёт клиенту листать чужой порядок.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/bookings")]
public class AdminBookingsController : ApiController
{
    /// <summary>
    /// Возвращает страницу броней всех клиентов: фильтр статуса, поиск и keyset-курсор.
    /// </summary>
    /// <remarks>
    /// Поиск — подстрока email клиента (регистр не важен, <c>%</c> и <c>_</c> — литералы)
    /// или точный идентификатор брони: строка, разбирающаяся как <see cref="Guid"/>, ищет
    /// бронь по id. По имени и телефону клиента, email барбера и префиксу id поиск
    /// не выполняется.
    /// </remarks>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию) или имя <c>BookingStatus</c>
    /// в любом регистре: <c>Pending</c>, <c>Confirmed</c>, <c>Cancelled</c>,
    /// <c>Completed</c>, <c>NoShow</c>.</param>
    /// <param name="search">Подстрока email клиента или точный идентификатор брони.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminBookingResponse>>> Get(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<AdminBookingResponse>, GetAdminBookingsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBookingsQuery(status, search, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает карточку одной брони любого статуса для администратора.
    /// </summary>
    /// <remarks>
    /// Контекст тот же, что и в строке списка: клиент, барбер, услуга, салон с часовым поясом,
    /// цена и обе заметки. Отсутствующая бронь — <c>404 admin.bookings.notFound</c>.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult<AdminBookingResponse>> GetById(
        Guid bookingId,
        [FromServices] IQueryHandler<AdminBookingResponse, GetAdminBookingQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBookingQuery(bookingId),
            cancellationToken));
}
