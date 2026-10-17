using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberServices.ActivateBarberService;
using BarberSearch.Application.BarberServices.DeactivateBarberService;
using BarberSearch.Application.BarberServices.UpdateBarberServiceDuration;
using BarberSearch.Application.BarberServices.UpdateBarberServicePrice;
using BarberSearch.Contracts.BarberServices.UpdateBarberServiceDuration;
using BarberSearch.Contracts.BarberServices.UpdateBarberServicePrice;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Услуга барбера: цена, длительность и доступность.
/// </summary>
/// <remarks>
/// Услуга — дочерняя сущность агрегата <c>BarberProfile</c>, поэтому адрес идёт через профиль.
/// Добавление и удаление услуги живут в <see cref="BarberProfilesController"/>.
/// </remarks>
[ApiController]
[Route("api/barber-profiles/{barberProfileId:guid}/services")]
public class BarberServicesController : ApiController
{
    /// <summary>
    /// Меняет цену услуги барбера.
    /// </summary>
    [HttpPut("{barberServiceId:guid}/price")]
    public async Task<ActionResult> UpdatePrice(
        Guid barberProfileId,
        Guid barberServiceId,
        [FromBody] UpdateBarberServicePriceRequest request,
        [FromServices] ICommandHandler<UpdateBarberServicePriceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberServicePriceCommand(barberProfileId, barberServiceId, request.Price),
            cancellationToken));

    /// <summary>
    /// Меняет длительность услуги барбера.
    /// </summary>
    [HttpPut("{barberServiceId:guid}/duration")]
    public async Task<ActionResult> UpdateDuration(
        Guid barberProfileId,
        Guid barberServiceId,
        [FromBody] UpdateBarberServiceDurationRequest request,
        [FromServices] ICommandHandler<UpdateBarberServiceDurationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberServiceDurationCommand(
                barberProfileId,
                barberServiceId,
                request.DurationMinutes),
            cancellationToken));

    /// <summary>
    /// Включает услугу барбера.
    /// </summary>
    [HttpPost("{barberServiceId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid barberProfileId,
        Guid barberServiceId,
        [FromServices] ICommandHandler<ActivateBarberServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ActivateBarberServiceCommand(barberProfileId, barberServiceId),
            cancellationToken));

    /// <summary>
    /// Выключает услугу барбера.
    /// </summary>
    [HttpDelete("{barberServiceId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid barberProfileId,
        Guid barberServiceId,
        [FromServices] ICommandHandler<DeactivateBarberServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeactivateBarberServiceCommand(barberProfileId, barberServiceId),
            cancellationToken));
}
