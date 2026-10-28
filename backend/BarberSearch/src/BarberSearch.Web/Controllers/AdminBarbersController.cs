using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.Admin.GetAdminBarber;
using BarberSearch.Application.BarberProfiles.Admin.GetAdminBarbers;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админское чтение барберов: очередь проверки и карточка с чек-листом готовности.
/// </summary>
/// <remarks>
/// Отдельный контроллер, потому что контракт отличается от публичного поиска: в список входят
/// неподтверждённые и скрытые профили, а в карточку — email владельца. Все действия закрыты
/// политикой <c>AdminOnly</c>; публичные <c>GET api/barbers</c> и <c>GET api/barbers/{id}</c>
/// не меняются.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/barbers")]
public class AdminBarbersController : ApiController
{
    /// <summary>
    /// Возвращает страницу барберов: все, на проверке (FIFO) или скрытые из поиска.
    /// </summary>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию), <c>pending</c> или <c>hidden</c>.</param>
    /// <param name="search">Поиск по имени или email.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminBarberResponse>>> Get(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<AdminBarberResponse>, GetAdminBarbersQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBarbersQuery(status, search, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает одного барбера любого статуса для админской карточки.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{barberProfileId:guid}")]
    public async Task<ActionResult<AdminBarberDetailResponse>> GetById(
        Guid barberProfileId,
        [FromServices] IQueryHandler<AdminBarberDetailResponse, GetAdminBarberQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminBarberQuery(barberProfileId),
            cancellationToken));
}
