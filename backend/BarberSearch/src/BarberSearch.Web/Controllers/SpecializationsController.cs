using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Specializations.ActivateSpecialization;
using BarberSearch.Application.Specializations.CreateSpecialization;
using BarberSearch.Application.Specializations.DeactivateSpecialization;
using BarberSearch.Application.Specializations.GetSpecializations;
using BarberSearch.Application.Specializations.RenameSpecialization;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Specializations.CreateSpecialization;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Contracts.Specializations.RenameSpecialization;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Специализации — справочник тегов барбера.
/// </summary>
/// <remarks>
/// Имя уникально без учёта регистра: повтор возвращает 409 (проверка в обработчике
/// плюс уникальный индекс <c>ux_specializations_name</c>). Каталог — админский справочник:
/// создание, переименование и активация доступны только администратору. Публичное
/// чтение одно — список активных строк для фильтра витрины (анонимно, без пагинации).
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/specializations")]
public class SpecializationsController : ApiController
{
    /// <summary>
    /// Публичный справочник активных специализаций для фильтров витрины.
    /// </summary>
    /// <remarks>
    /// Анонимное действие: классовый <c>[Authorize(AdminOnly)]</c> перекрыт
    /// <c>[AllowAnonymous]</c>. Лимит — <c>reads</c> 120 запросов в минуту на адрес клиента.
    /// Ответ — голый JSON-массив без пагинации; пустой справочник — <c>200 []</c>.
    /// </remarks>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SpecializationResponse>>> GetAll(
        [FromServices] IQueryHandler<IReadOnlyList<SpecializationResponse>, GetSpecializationsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetSpecializationsQuery(),
            cancellationToken));

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
