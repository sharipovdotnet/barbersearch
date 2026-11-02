using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Admin.GetAdminSummary;
using BarberSearch.Contracts.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админская сводка «что требует внимания»: очереди отзывов, барберов и салонов.
/// </summary>
/// <remarks>
/// <para>
/// Это единственное агрегатное чтение админки и единственное действие контроллера: параметров,
/// пагинации и курсора у него нет. Существующие списки не меняются — <c>total</c> в
/// <c>PagedResponse</c> не появляется, число очереди нужно одному экрану.
/// </para>
/// <para>
/// Число каждой очереди совпадает с фильтром её раздела: <c>is_published = false</c>
/// («На модерации», включая скрытые администратором отзывы — в домене это один флаг),
/// <c>is_verified = false</c> (<c>AdminBarberStatus.Pending</c>) и <c>is_active = false</c>
/// (<c>AdminCatalogueStatus.Hidden</c> для салонов). Порядок и глубина очередей остаются
/// за <c>GET api/admin/reviews</c>, <c>GET api/admin/barbers</c> и админским каталогом.
/// </para>
/// <para>
/// Сводка только читает: действий, мутаций и записи в <c>audit_log</c> у неё нет. Email
/// и телефон клиента в ответ не входят — только имена; полный контекст визита и PII
/// остаются в <c>GET api/admin/reviews</c>.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/summary")]
public class AdminSummaryController : ApiController
{
    /// <summary>
    /// Возвращает счётчики трёх очередей и самый старый отзыв модерации.
    /// </summary>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<AdminSummaryResponse>> Get(
        [FromServices] IQueryHandler<AdminSummaryResponse, GetAdminSummaryQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new GetAdminSummaryQuery(), cancellationToken));
}
