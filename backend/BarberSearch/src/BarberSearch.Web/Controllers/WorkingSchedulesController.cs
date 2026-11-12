using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;
using BarberSearch.Application.WorkingSchedules.SetWorkingScheduleWorkingHours;
using BarberSearch.Contracts.WorkingSchedules.SetWorkingScheduleWorkingHours;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Одна запись расписания пары (барбер, салон): рабочие часы и выходной.
/// </summary>
/// <remarks>
/// Добавление и удаление записи живут в <see cref="BarberSalonsController"/>: там создаётся
/// и уничтожается сама запись, здесь меняется её содержимое.
/// <para>
/// Обе ручки проверяют активные визиты на изменяемых датах дня недели и отвечают
/// <c>409 barberSchedule.conflict</c> со списком затронутых визитов; <c>force=true</c>
/// сохраняет правку. Невалидное значение <c>force</c> трактуется как <c>false</c>:
/// отдельного <c>400</c> нет.
/// </para>
/// </remarks>
[ApiController]
[Route("api/barber-profiles/{barberProfileId:guid}/salons/{salonId:guid}/working-schedules")]
public class WorkingSchedulesController : ApiController
{
    /// <summary>
    /// Задаёт рабочие часы. Установка часов снимает признак выходного дня — правило домена.
    /// </summary>
    /// <param name="force">Сохранить, даже если активные визиты выйдут за новые часы.</param>
    [HttpPut("{workingScheduleId:guid}/hours")]
    public async Task<ActionResult> SetWorkingHours(
        Guid barberProfileId,
        Guid salonId,
        Guid workingScheduleId,
        [FromQuery] string? force,
        [FromBody] SetWorkingScheduleWorkingHoursRequest request,
        [FromServices] ICommandHandler<ScheduleWriteOutcome, SetWorkingScheduleWorkingHoursCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new SetWorkingScheduleWorkingHoursCommand(
                barberProfileId,
                salonId,
                workingScheduleId,
                request.StartTime,
                request.EndTime,
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
    /// Помечает запись расписания выходным днём.
    /// </summary>
    /// <param name="force">Сохранить, даже если на этих датах есть активные визиты.</param>
    [HttpPut("{workingScheduleId:guid}/day-off")]
    public async Task<ActionResult> SetDayOff(
        Guid barberProfileId,
        Guid salonId,
        Guid workingScheduleId,
        [FromQuery] string? force,
        [FromServices] ICommandHandler<ScheduleWriteOutcome, SetWorkingScheduleDayOffCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new SetWorkingScheduleDayOffCommand(barberProfileId, salonId, workingScheduleId, ReadForce(force)),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        return result.Value.Conflicts.Count > 0
            ? FailureProblemDetails.CreateConflictResult(result.Value.Conflicts)
            : NoContent();
    }
}
