using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.ActivateBarberProfile;
using BarberSearch.Application.BarberProfiles.AddBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.AddBarberProfileService;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;
using BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ChangeBarberProfileGrade;
using BarberSearch.Application.BarberProfiles.CreateBarberProfile;
using BarberSearch.Application.BarberProfiles.DeactivateBarberProfile;
using BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileSpecialization;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.SetBarberProfilePrimarySalon;
using BarberSearch.Application.BarberProfiles.UnverifyBarberProfile;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfileRating;
using BarberSearch.Application.BarberProfiles.VerifyBarberProfile;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfilePhoto;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.ChangeBarberProfileGrade;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhoto;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileRating;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Профиль барбера — центр агрегата: сам профиль, грейд, рейтинг, верификация,
/// портфолио, салоны, услуги, специализации и отпуска.
/// </summary>
/// <remarks>
/// Дочерние сущности адресуются через профиль (<c>/api/barber-profiles/{id}/...</c>):
/// они правятся только через агрегат-корень, и маршрут это отражает.
/// </remarks>
[ApiController]
[Route("api/barber-profiles")]
public class BarberProfilesController : ApiController
{
    /// <summary>
    /// Создаёт профиль барбера.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] BarberProfileCreateRequest request,
        [FromServices] ICommandHandler<Guid, CreateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(
            new CreateBarberProfileCommand(request),
            cancellationToken));

    /// <summary>
    /// Обновляет профиль барбера. Грейда в теле нет: его меняет только административный сценарий.
    /// </summary>
    [HttpPut("{barberProfileId:guid}")]
    public async Task<ActionResult> Update(
        Guid barberProfileId,
        [FromBody] BarberProfileUpdateRequest request,
        [FromServices] ICommandHandler<UpdateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberProfileCommand(barberProfileId, request),
            cancellationToken));

    /// <summary>
    /// Меняет грейд барбера.
    /// </summary>
    [HttpPut("{barberProfileId:guid}/grade")]
    public async Task<ActionResult> ChangeGrade(
        Guid barberProfileId,
        [FromBody] ChangeBarberProfileGradeRequest request,
        [FromServices] ICommandHandler<ChangeBarberProfileGradeCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ChangeBarberProfileGradeCommand(barberProfileId, request.Grade),
            cancellationToken));

    /// <summary>
    /// Пересчитывает рейтинг барбера по отзывам.
    /// </summary>
    [HttpPut("{barberProfileId:guid}/rating")]
    public async Task<ActionResult> UpdateRating(
        Guid barberProfileId,
        [FromBody] UpdateBarberProfileRatingRequest request,
        [FromServices] ICommandHandler<UpdateBarberProfileRatingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberProfileRatingCommand(barberProfileId, request.Rating, request.ReviewsCount),
            cancellationToken));

    /// <summary>
    /// Активирует профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid barberProfileId,
        [FromServices] ICommandHandler<ActivateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ActivateBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Деактивирует профиль барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid barberProfileId,
        [FromServices] ICommandHandler<DeactivateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeactivateBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Подтверждает профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/verification")]
    public async Task<ActionResult> Verify(
        Guid barberProfileId,
        [FromServices] ICommandHandler<VerifyBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new VerifyBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Снимает подтверждение профиля барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/verification")]
    public async Task<ActionResult> Unverify(
        Guid barberProfileId,
        [FromServices] ICommandHandler<UnverifyBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnverifyBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Добавляет фотографию в портфолио барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/photos")]
    public async Task<ActionResult> AddPhoto(
        Guid barberProfileId,
        [FromBody] AddBarberProfilePhotoRequest request,
        [FromServices] ICommandHandler<AddBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfilePhotoCommand(
                barberProfileId,
                request.Url,
                request.ThumbnailUrl,
                request.Description),
            cancellationToken));

    /// <summary>
    /// Удаляет фотографию из портфолио барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult> RemovePhoto(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<RemoveBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfilePhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Меняет порядок фотографии в портфолио барбера (порядок задаёт агрегат-корень).
    /// </summary>
    [HttpPut("{barberProfileId:guid}/photos/{photoId:guid}/sort-order")]
    public async Task<ActionResult> ReorderPhoto(
        Guid barberProfileId,
        Guid photoId,
        [FromBody] ReorderBarberProfilePhotoRequest request,
        [FromServices] ICommandHandler<ReorderBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ReorderBarberProfilePhotoCommand(barberProfileId, photoId, request.SortOrder),
            cancellationToken));

    /// <summary>
    /// Добавляет салон в профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/salons")]
    public async Task<ActionResult> AddSalon(
        Guid barberProfileId,
        [FromBody] AddBarberProfileSalonRequest request,
        [FromServices] ICommandHandler<AddBarberProfileSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileSalonCommand(barberProfileId, request.SalonId, request.IsPrimary),
            cancellationToken));

    /// <summary>
    /// Удаляет салон из профиля барбера вместе с его расписанием.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/salons/{salonId:guid}")]
    public async Task<ActionResult> RemoveSalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] ICommandHandler<RemoveBarberProfileSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileSalonCommand(barberProfileId, salonId),
            cancellationToken));

    /// <summary>
    /// Назначает салон основным. Признак снимается с прежнего основного — правило домена.
    /// </summary>
    [HttpPut("{barberProfileId:guid}/salons/{salonId:guid}/primary")]
    public async Task<ActionResult> SetPrimarySalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] ICommandHandler<SetBarberProfilePrimarySalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SetBarberProfilePrimarySalonCommand(barberProfileId, salonId),
            cancellationToken));

    /// <summary>
    /// Возвращает салон барбера вместе с расписанием пары (барбер, салон).
    /// </summary>
    [HttpGet("{barberProfileId:guid}/salons/{salonId:guid}")]
    public async Task<ActionResult<BarberSalonResponse>> FindSalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] IQueryHandler<BarberSalon, FindBarberProfileSalonQuery> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new FindBarberProfileSalonQuery(barberProfileId, salonId),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        return Ok(ToResponse(result.Value));
    }

    /// <summary>
    /// Добавляет услугу в профиль барбера: цена и длительность задаются для этого барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/services")]
    public async Task<ActionResult> AddService(
        Guid barberProfileId,
        [FromBody] AddBarberProfileServiceRequest request,
        [FromServices] ICommandHandler<AddBarberProfileServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileServiceCommand(
                barberProfileId,
                request.HairServiceId,
                request.Price,
                request.DurationMinutes),
            cancellationToken));

    /// <summary>
    /// Удаляет услугу из профиля барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/services/{hairServiceId:guid}")]
    public async Task<ActionResult> RemoveService(
        Guid barberProfileId,
        Guid hairServiceId,
        [FromServices] ICommandHandler<RemoveBarberProfileServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileServiceCommand(barberProfileId, hairServiceId),
            cancellationToken));

    /// <summary>
    /// Добавляет специализацию в профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/specializations/{specializationId:guid}")]
    public async Task<ActionResult> AddSpecialization(
        Guid barberProfileId,
        Guid specializationId,
        [FromServices] ICommandHandler<AddBarberProfileSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileSpecializationCommand(barberProfileId, specializationId),
            cancellationToken));

    /// <summary>
    /// Удаляет специализацию из профиля барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/specializations/{specializationId:guid}")]
    public async Task<ActionResult> RemoveSpecialization(
        Guid barberProfileId,
        Guid specializationId,
        [FromServices] ICommandHandler<RemoveBarberProfileSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileSpecializationCommand(barberProfileId, specializationId),
            cancellationToken));

    /// <summary>
    /// Добавляет нерабочий период (отпуск) барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/time-offs")]
    public async Task<ActionResult> AddTimeOff(
        Guid barberProfileId,
        [FromBody] AddBarberProfileTimeOffRequest request,
        [FromServices] ICommandHandler<AddBarberProfileTimeOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileTimeOffCommand(
                barberProfileId,
                request.StartAt,
                request.EndAt,
                request.Reason),
            cancellationToken));

    /// <summary>
    /// Удаляет нерабочий период (отпуск) барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/time-offs/{timeOffId:guid}")]
    public async Task<ActionResult> RemoveTimeOff(
        Guid barberProfileId,
        Guid timeOffId,
        [FromServices] ICommandHandler<RemoveBarberProfileTimeOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileTimeOffCommand(barberProfileId, timeOffId),
            cancellationToken));

    /// <summary>
    /// Перекладывает доменный салон в ответ API: навигация на профиль наружу не выходит.
    /// </summary>
    private static BarberSalonResponse ToResponse(BarberSalon salon)
        => new(
            salon.Id,
            salon.BarberProfileId,
            salon.SalonId,
            salon.IsPrimary,
            [.. salon.WorkingSchedules.Select(schedule => new WorkingScheduleResponse(
                schedule.Id,
                schedule.DayOfWeek,
                schedule.StartTime,
                schedule.EndTime,
                schedule.IsDayOff))]);
}
