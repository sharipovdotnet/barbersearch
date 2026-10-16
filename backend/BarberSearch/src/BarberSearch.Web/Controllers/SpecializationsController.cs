using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Specializations.ActivateSpecialization;
using BarberSearch.Application.Specializations.CreateSpecialization;
using BarberSearch.Application.Specializations.DeactivateSpecialization;
using BarberSearch.Application.Specializations.RenameSpecialization;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Specializations.CreateSpecialization;
using BarberSearch.Contracts.Specializations.RenameSpecialization;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Специализации — справочник тегов барбера.
/// </summary>
/// <remarks>
/// Имя уникально без учёта регистра: повтор возвращает 409 (проверка в обработчике
/// плюс уникальный индекс <c>ux_specializations_name</c>).
/// </remarks>
[ApiController]
[Route("api/specializations")]
public class SpecializationsController : ApiController
{
    /// <summary>
    /// Создаёт специализацию.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateSpecializationRequest request,
        [FromServices] ICommandHandler<Guid, CreateSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(
            new CreateSpecializationCommand(request.Name, request.Description),
            cancellationToken));

    /// <summary>
    /// Переименовывает специализацию и меняет её описание.
    /// </summary>
    [HttpPut("{specializationId:guid}")]
    public async Task<ActionResult> Rename(
        Guid specializationId,
        [FromBody] RenameSpecializationRequest request,
        [FromServices] ICommandHandler<RenameSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RenameSpecializationCommand(specializationId, request.Name, request.Description),
            cancellationToken));

    /// <summary>
    /// Включает специализацию в справочнике.
    /// </summary>
    [HttpPost("{specializationId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid specializationId,
        [FromServices] ICommandHandler<ActivateSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ActivateSpecializationCommand(specializationId),
            cancellationToken));

    /// <summary>
    /// Выключает специализацию в справочнике. Имя за выключенной записью сохраняется.
    /// </summary>
    [HttpDelete("{specializationId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid specializationId,
        [FromServices] ICommandHandler<DeactivateSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeactivateSpecializationCommand(specializationId),
            cancellationToken));
}
