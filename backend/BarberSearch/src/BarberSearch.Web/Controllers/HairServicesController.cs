using BarberSearch.Application.Abstractions;
using BarberSearch.Application.HairServices.ActivateHairService;
using BarberSearch.Application.HairServices.CreateHairService;
using BarberSearch.Application.HairServices.DeactivateHairService;
using BarberSearch.Application.HairServices.UpdateHairService;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.CreateHairService;
using BarberSearch.Contracts.HairServices.UpdateHairService;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Каталог услуг: создание, правка, активность.
/// </summary>
/// <remarks>
/// Цена и длительность конкретного барбера живут в профиле барбера, а не здесь:
/// каталог хранит только длительность по умолчанию. Каталог — админский справочник:
/// создание, правка и активация доступны только администратору.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/hair-services")]
public class HairServicesController : ApiController
{
    /// <summary>
    /// Создаёт услугу каталога.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateHairServiceRequest request,
        [FromServices] ICommandHandler<Guid, CreateHairServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(
            new CreateHairServiceCommand(
                request.Name,
                request.Category,
                request.DefaultDurationMinutes,
                request.Description),
            cancellationToken));

    /// <summary>
    /// Меняет услугу каталога. Категория не меняется — это правило Application.
    /// </summary>
    [HttpPut("{hairServiceId:guid}")]
    public async Task<ActionResult> Update(
        Guid hairServiceId,
        [FromBody] UpdateHairServiceRequest request,
        [FromServices] ICommandHandler<UpdateHairServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateHairServiceCommand(
                hairServiceId,
                request.Name,
                request.Description,
                request.DefaultDurationMinutes),
            cancellationToken));

    /// <summary>
    /// Включает услугу каталога.
    /// </summary>
    [HttpPost("{hairServiceId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid hairServiceId,
        [FromServices] ICommandHandler<ActivateHairServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ActivateHairServiceCommand(hairServiceId),
            cancellationToken));

    /// <summary>
    /// Выключает услугу каталога.
    /// </summary>
    [HttpDelete("{hairServiceId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid hairServiceId,
        [FromServices] ICommandHandler<DeactivateHairServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeactivateHairServiceCommand(hairServiceId),
            cancellationToken));
}
