using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.GetBarberSchedule;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.BarberSalons.RemoveScheduleException;
using BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Application.BarberSalons.SetScheduleException;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Domain.WorkingSchedules;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Расписание барбера: чтение для экрана, замена недели и исключения на даты.
/// </summary>
/// <remarks>
/// Владение проверяют обработчики через <c>EnsureCanManage</c>: владелец профиля или
/// администратор. Публичная карточка <c>GET api/barbers/{id}</c> здесь не используется,
/// потому что она скрывает неподтверждённые профили, а расписание заполняют до верификации.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/barber-profiles/{barberProfileId:guid}")]
public class BarberScheduleController : ApiController
{
    /// <summary>
    /// Расписание барбера: салоны, недельный шаблон, исключения и отпуска.
    /// </summary>
    [HttpGet("schedule")]
    public async Task<ActionResult<BarberScheduleResponse>> Get(
        Guid barberProfileId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IQueryHandler<BarberScheduleResponse, GetBarberScheduleQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberScheduleQuery(barberProfileId, from, to),
            cancellationToken));

    /// <summary>
    /// Полностью заменяет недельный шаблон пары (барбер, салон).
    /// </summary>
    /// <remarks>
    /// Если правка оставляет активные визиты вне новых часов, отвечает <c>409
    /// barberSchedule.conflict</c> со списком затронутых визитов. <c>force=true</c> сохраняет
    /// правку, не отключая проверку прав и существования. Невалидное значение <c>force</c>
    /// трактуется как <c>false</c>: отдельного <c>400</c> нет.
    /// </remarks>
    /// <param name="force">Сохранить, даже если активные визиты выйдут за новые часы.</param>
    [HttpPut("salons/{salonId:guid}/working-schedules")]
    public async Task<ActionResult> ReplaceWorkingSchedules(
        Guid barberProfileId,
        Guid salonId,
        [FromQuery] string? force,
        [FromBody] ReplaceWorkingSchedulesRequest request,
        [FromServices] ICommandHandler<ScheduleWriteOutcome, ReplaceWorkingSchedulesCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new ReplaceWorkingSchedulesCommand(
                barberProfileId,
                salonId,
                [.. (request.Days ?? []).Select(day => new WorkingDaySchedule(
                    day.DayOfWeek,
                    day.IsDayOff,
                    day.StartTime,
                    day.EndTime))],
                ReadForce(force)),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        return result.Value.Conflicts.Count > 0
            ? FailureProblemDetails.CreateConflictResult(result.Value.Conflicts)
            : NoContent();
    }

    /// <summary>
    /// Ставит или переписывает исключение на дату.
    /// </summary>
    /// <remarks>
    /// Явная дата проверяется и за горизонтом 60 дней. Если на дате есть активные визиты
    /// и правка делает их невозможными, отвечает <c>409 barberSchedule.conflict</c>.
    /// <c>force=true</c> сохраняет правку. Невалидное значение <c>force</c> трактуется
    /// как <c>false</c>: отдельного <c>400</c> нет.
    /// </remarks>
    /// <param name="force">Сохранить, даже если на дате есть активные визиты.</param>
    [HttpPut("salons/{salonId:guid}/schedule-exceptions/{date}")]
    public async Task<ActionResult> SetScheduleException(
        Guid barberProfileId,
        Guid salonId,
        DateOnly date,
        [FromQuery] string? force,
        [FromBody] SetScheduleExceptionRequest request,
        [FromServices] ICommandHandler<ScheduleWriteOutcome, SetScheduleExceptionCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new SetScheduleExceptionCommand(
                barberProfileId,
                salonId,
                date,
                request.IsDayOff,
                request.StartTime,
                request.EndTime,
                request.Reason,
                ReadForce(force)),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        return result.Value.Conflicts.Count > 0
            ? FailureProblemDetails.CreateConflictResult(result.Value.Conflicts)
            : NoContent();
    }

    /// <summary>
    /// Снимает исключение с даты.
    /// </summary>
    [HttpDelete("salons/{salonId:guid}/schedule-exceptions/{date}")]
    public async Task<ActionResult> RemoveScheduleException(
        Guid barberProfileId,
        Guid salonId,
        DateOnly date,
        [FromServices] ICommandHandler<RemoveScheduleExceptionCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveScheduleExceptionCommand(barberProfileId, salonId, date),
            cancellationToken));
}
