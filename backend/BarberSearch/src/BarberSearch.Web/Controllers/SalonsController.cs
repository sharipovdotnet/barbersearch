using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Salons.ActivateSalon;
using BarberSearch.Application.Salons.CreateSalon;
using BarberSearch.Application.Salons.DeactivateSalon;
using BarberSearch.Application.Salons.GetSalonDirectory;
using BarberSearch.Application.Salons.UpdateSalonContactInfo;
using BarberSearch.Application.Salons.UpdateSalonLocation;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Салоны: публичный каталог для выбора и админское управление.
/// </summary>
/// <remarks>
/// Расписание и услуги салона здесь не живут: слоты считаются по паре (барбер, салон),
/// поэтому расписание адресуется через профиль барбера. Чтение каталога открыто анонимно
/// (барбер выбирает салон до входа в кабинет, а витрина — без входа); создание, правка
/// и активация по-прежнему доступны только администратору.
/// </remarks>
[ApiController]
[Route("api/salons")]
public class SalonsController : ApiController
{
    /// <summary>
    /// Публичный каталог активных салонов: фильтр по городу и поиск по названию и адресу.
    /// </summary>
    /// <remarks>
    /// Анонимное действие с лимитом <c>reads</c> 120 запросов в минуту на адрес клиента.
    /// Ответ — голый JSON-массив без пагинации: справочник для выбора, а не пейджер.
    /// </remarks>
    /// <param name="city">Город; пустая строка означает «без фильтра».</param>
    /// <param name="search">Поиск по названию и адресу; пустая строка — без фильтра.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalonDirectoryItemResponse>>> GetDirectory(
        [FromQuery] string? city,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<IReadOnlyList<SalonDirectoryItemResponse>, GetSalonDirectoryQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetSalonDirectoryQuery(city, search),
            cancellationToken));

    /// <summary>
    /// Создаёт салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateSalonRequest request,
        [FromServices] ICommandHandler<Guid, CreateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(new CreateSalonCommand(request), cancellationToken));

    /// <summary>
    /// Меняет адрес и координаты салона.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{salonId:guid}/location")]
    public async Task<ActionResult> UpdateLocation(
        Guid salonId,
        [FromBody] UpdateSalonLocationRequest request,
        [FromServices] ICommandHandler<UpdateSalonLocationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateSalonLocationCommand(
                salonId,
                request.Address,
                request.City,
                request.Country,
                request.TimeZoneId,
                request.Latitude,
                request.Longitude,
                request.PostalCode),
            cancellationToken));

    /// <summary>
    /// Меняет контактные данные салона.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{salonId:guid}/contact-info")]
    public async Task<ActionResult> UpdateContactInfo(
        Guid salonId,
        [FromBody] UpdateSalonContactInfoRequest request,
        [FromServices] ICommandHandler<UpdateSalonContactInfoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateSalonContactInfoCommand(salonId, request.PhoneNumber, request.WebsiteUrl),
            cancellationToken));

    /// <summary>
    /// Активирует салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{salonId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid salonId,
        [FromServices] ICommandHandler<ActivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ActivateSalonCommand(salonId), cancellationToken));

    /// <summary>
    /// Деактивирует салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{salonId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid salonId,
        [FromServices] ICommandHandler<DeactivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new DeactivateSalonCommand(salonId), cancellationToken));
}
