using BarberSearch.Application.Abstractions;
using BarberSearch.Application.HairServices.Admin.GetAdminHairServices;
using BarberSearch.Application.Salons.Admin.GetAdminSalons;
using BarberSearch.Application.Specializations.Admin.GetAdminSpecializations;
using BarberSearch.Contracts.HairServices.Admin;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Contracts.Specializations.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админское чтение каталога: услуги, специализации и салоны.
/// </summary>
/// <remarks>
/// Отдельный контроллер, потому что публичные чтения справочников отдают только активные
/// строки, а у салонов чтения не было вовсе: администратор не мог даже перечислить строки,
/// чтобы отредактировать или выключить их. Все действия закрыты политикой <c>AdminOnly</c>.
/// Изменяющие операции остаются на <c>HairServicesController</c>, <c>SpecializationsController</c>
/// и <c>SalonsController</c> и работают без изменений: здесь только чтения.
/// <para>
/// Ответ — полный справочник, а не страница: словари на десятки строк, а созданная строка
/// должна быть видна сразу, а не на «странице 3». Пагинация справочников — отдельное решение,
/// когда они перестанут быть маленькими.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/catalogue")]
public class AdminCatalogueController : ApiController
{
    /// <summary>
    /// Возвращает справочник услуг, включая выключенные строки, с фильтром статуса.
    /// </summary>
    /// <remarks>
    /// Поиск идёт по названию. Ответ — голый JSON-массив, порядок — категория, название,
    /// идентификатор. <c>usageCount</c> — число активных подтверждённых барберов, у которых
    /// есть включённая связь с услугой.
    /// </remarks>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию), <c>active</c> или <c>hidden</c>.</param>
    /// <param name="search">Поиск по названию; не длиннее 100 символов.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("hair-services")]
    public async Task<ActionResult<IReadOnlyList<AdminHairServiceResponse>>> GetHairServices(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<
            IReadOnlyList<AdminHairServiceResponse>,
            GetAdminHairServicesQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminHairServicesQuery(status, search),
            cancellationToken));

    /// <summary>
    /// Возвращает справочник специализаций, включая выключенные строки, с фильтром статуса.
    /// </summary>
    /// <remarks>
    /// Поиск идёт по названию. Ответ — голый JSON-массив, порядок — название, идентификатор.
    /// <c>usageCount</c> — число активных подтверждённых барберов, указавших специализацию.
    /// </remarks>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию), <c>active</c> или <c>hidden</c>.</param>
    /// <param name="search">Поиск по названию; не длиннее 100 символов.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("specializations")]
    public async Task<ActionResult<IReadOnlyList<AdminSpecializationResponse>>> GetSpecializations(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<
            IReadOnlyList<AdminSpecializationResponse>,
            GetAdminSpecializationsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminSpecializationsQuery(status, search),
            cancellationToken));

    /// <summary>
    /// Возвращает справочник салонов, включая выключенные строки, с фильтром статуса.
    /// </summary>
    /// <remarks>
    /// Поиск идёт по названию и городу. Ответ — голый JSON-массив, порядок — город, название,
    /// идентификатор; строки несут все поля формы правки. <c>usageCount</c> — число активных
    /// подтверждённых барберов, работающих в салоне.
    /// </remarks>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию), <c>active</c> или <c>hidden</c>.</param>
    /// <param name="search">Поиск по названию или городу; не длиннее 100 символов.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("salons")]
    public async Task<ActionResult<IReadOnlyList<AdminSalonResponse>>> GetSalons(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<
            IReadOnlyList<AdminSalonResponse>,
            GetAdminSalonsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminSalonsQuery(status, search),
            cancellationToken));
}
