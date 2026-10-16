using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Salons.ActivateSalon;
using BarberSearch.Application.Salons.CreateSalon;
using BarberSearch.Application.Salons.DeactivateSalon;
using BarberSearch.Application.Salons.UpdateSalonContactInfo;
using BarberSearch.Application.Salons.UpdateSalonLocation;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Салоны: создание, адрес, контакты и активность.
/// </summary>
/// <remarks>
/// Расписание и услуги салона здесь не живут: слоты считаются по паре (барбер, салон),
/// поэтому расписание адресуется через профиль барбера.
/// </remarks>
[ApiController]
[Route("api/salons")]
public class SalonsController : ApiController
{
    /// <summary>
    /// Создаёт салон.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateSalonRequest request,
        [FromServices] ICommandHandler<Guid, CreateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(new CreateSalonCommand(request), cancellationToken));

    /// <summary>
    /// Меняет адрес и координаты салона.
    /// </summary>
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
    [HttpPost("{salonId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid salonId,
        [FromServices] ICommandHandler<ActivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ActivateSalonCommand(salonId), cancellationToken));

    /// <summary>
    /// Деактивирует салон.
    /// </summary>
    [HttpDelete("{salonId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid salonId,
        [FromServices] ICommandHandler<DeactivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new DeactivateSalonCommand(salonId), cancellationToken));
}
