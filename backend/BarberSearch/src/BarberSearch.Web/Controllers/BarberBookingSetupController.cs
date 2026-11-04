using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberClients;
using BarberSearch.Application.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Данные формы «Записать клиента» в кабинете барбера: поиск клиента, услуги и салоны,
/// свободное время собственного профиля.
/// </summary>
/// <remarks>
/// Все три действия закрыты политикой <c>BarberOnly</c> и берут профиль из claim'а
/// <c>barber_profile_id</c>: query-параметра с чужим профилем нет, поэтому прочитать
/// контакты клиентов или слоты другого барбера нельзя. Это чтения, CSRF им не нужен;
/// создание самой брони живёт в <c>POST api/barber/bookings</c>.
/// </remarks>
[ApiController]
[Route("api/barber")]
[Authorize(Policy = AuthorizationPolicies.BarberOnly)]
public class BarberBookingSetupController : ApiController
{
    /// <summary>
    /// Ищет зарегистрированного клиента по email или телефону.
    /// </summary>
    /// <remarks>
    /// Минимум три символа и максимум сто проверяет Application; ответ ограничен
    /// десятью строками. Возвращаются только активные пользователи с ролью
    /// <c>Customer</c> — контакты администраторов и барберов форме не нужны.
    /// </remarks>
    /// <param name="search">Email или телефон клиента.</param>
    /// <param name="handler">Обработчик поиска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<BarberClientResponse>>> SearchClients(
        [FromQuery] string? search,
        [FromServices] IQueryHandler<IReadOnlyList<BarberClientResponse>, SearchBarberClientsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SearchBarberClientsQuery(search),
            cancellationToken));

    /// <summary>
    /// Возвращает активные услуги и салоны собственного профиля барбера.
    /// </summary>
    /// <remarks>
    /// Публичная карточка отдаёт только подтверждённый профиль, а записать клиента
    /// можно и до публикации, поэтому коллекции читаются из собственного агрегата.
    /// </remarks>
    /// <param name="currentUser">Текущая сессия: профиль барбера из claim'а.</param>
    /// <param name="handler">Обработчик чтения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("booking-options")]
    public async Task<ActionResult<BarberBookingOptionsResponse>> GetBookingOptions(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IQueryHandler<BarberBookingOptionsResponse, GetBarberBookingOptionsQuery> handler,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetBarberProfileId(out var barberProfileId))
        {
            return Forbid();
        }

        return ToActionResult(await handler.Handle(
            new GetBarberBookingOptionsQuery(barberProfileId),
            cancellationToken));
    }

    /// <summary>
    /// Возвращает свободное время собственного профиля на локальную дату салона.
    /// </summary>
    /// <remarks>
    /// Отличие от публичного <c>GET api/barbers/{id}/slots</c> только в проверке
    /// видимости профиля: кабинет работает и до подтверждения. Активность салона
    /// и услуги, отпуска и занятые интервалы учитываются одинаково.
    /// </remarks>
    /// <param name="date">Локальная дата салона <c>YYYY-MM-DD</c>.</param>
    /// <param name="hairServiceId">Услуга справочника; задаёт шаг сетки.</param>
    /// <param name="salonId">Салон; по умолчанию основной.</param>
    /// <param name="currentUser">Текущая сессия: профиль барбера из claim'а.</param>
    /// <param name="handler">Обработчик расчёта слотов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("slots")]
    public async Task<ActionResult<BarberSlotsResponse>> GetSlots(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? hairServiceId,
        [FromQuery] Guid? salonId,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IQueryHandler<BarberSlotsResponse, GetBarberSlotsQuery> handler,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetBarberProfileId(out var barberProfileId))
        {
            return Forbid();
        }

        return ToActionResult(await handler.Handle(
            new GetBarberSlotsQuery(
                barberProfileId,
                date,
                hairServiceId,
                salonId,
                RequireVisibleProfile: false),
            cancellationToken));
    }
}
