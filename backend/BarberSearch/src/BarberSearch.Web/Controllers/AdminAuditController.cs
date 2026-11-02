using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Audit.Admin.GetAdminAuditLog;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Журнал изменений: read-only лента аудита для администратора.
/// </summary>
/// <remarks>
/// <para>
/// Журнал неизменяем: здесь есть только <c>GET</c>, и <c>POST</c>/<c>PUT</c>/<c>DELETE</c>
/// не появятся — «отменить изменение» или «очистить журнал» противоречат смыслу аудита.
/// Записи пишет сервер при сохранении изменений, а не клиент.
/// </para>
/// <para>
/// В журнал попадают только <c>User</c>, <c>BarberProfile</c> и <c>Review</c> — allowlist
/// <c>AuditTrail</c>. Правки каталога, салонов, услуг, специализаций и броней в журнал
/// <b>не</b> попадают; это известное ограничение, и UI не обещает обратного.
/// </para>
/// <para>
/// Имя сущности не резолвится: запись хранит <c>entity_type</c> и <c>entity_id</c>, а имя
/// могло быть переименовано или удалено — «актуальное» имя рядом с историческим diff'ом
/// вводило бы в заблуждение. Порядок — свежие сверху, страницы — keyset по
/// <c>(occurred_at, id)</c>.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/audit")]
public class AdminAuditController : ApiController
{
    /// <summary>
    /// Возвращает страницу журнала с фильтрами типа сущности, автора и периода.
    /// </summary>
    /// <param name="entityType">Тип сущности: <c>all</c> (по умолчанию), <c>user</c>,
    /// <c>barber_profile</c> или <c>review</c>.</param>
    /// <param name="actor">Подстрока email автора; пустое значение — без фильтра.</param>
    /// <param name="from">Начальная дата UTC-суток включительно в формате <c>yyyy-MM-dd</c>.</param>
    /// <param name="to">Последняя дата UTC-суток включительно в формате <c>yyyy-MM-dd</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminAuditEntryResponse>>> Get(
        [FromQuery] string? entityType,
        [FromQuery] string? actor,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<AdminAuditEntryResponse>, GetAdminAuditLogQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminAuditLogQuery(entityType, actor, from, to, cursor, pageSize),
            cancellationToken));
}
