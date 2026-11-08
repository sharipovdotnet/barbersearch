using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Admin.GetAdminStatistics;
using BarberSearch.Contracts.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админская статистика сервиса за период.
/// </summary>
/// <remarks>
/// <para>
/// Раздел только для администратора: <c>AdminOnly</c> на контроллере. Публичной
/// страницы «в цифрах» и статистики в кабинете барбера нет.
/// </para>
/// <para>
/// Период приходит строками <c>yyyy-MM-dd</c> и разбирается в обработчике, чтобы
/// кривой формат, отсутствующая дата, обратный порядок и период длиннее 366 дней
/// отвечали одним кодом <c>admin.statistics.period</c> (<c>400</c>). Ответ — 200,
/// <c>401</c> без входа, <c>403</c> без роли, <c>429</c> по лимиту и <c>500</c>;
/// <c>404</c> у статистики нет.
/// </para>
/// <para>
/// Только чтение: ни мутаций, ни записей в журнал изменений, ни кэша на сервере —
/// ответ помечен <c>no-store</c> общим middleware.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/statistics")]
public class AdminStatisticsController : ApiController
{
    /// <summary>
    /// Возвращает типизированные секции статистики за включительный период.
    /// </summary>
    /// <param name="from">Первый день периода, <c>yyyy-MM-dd</c>.</param>
    /// <param name="to">Последний день периода, <c>yyyy-MM-dd</c>.</param>
    /// <param name="compare">Сравнивать ли с предыдущим периодом той же длины.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<AdminStatisticsResponse>> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] bool compare,
        [FromServices] IQueryHandler<AdminStatisticsResponse, GetAdminStatisticsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminStatisticsQuery(from, to, compare),
            cancellationToken));
}
