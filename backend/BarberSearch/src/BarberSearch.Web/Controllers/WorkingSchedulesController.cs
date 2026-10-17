using BarberSearch.Application.Abstractions;
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
/// </remarks>
[ApiController]
[Route("api/barber-profiles/{barberProfileId:guid}/salons/{salonId:guid}/working-schedules")]
public class WorkingSchedulesController : ApiController
{
    /// <summary>
    /// Задаёт рабочие часы. Установка часов снимает признак выходного дня — правило домена.
    /// </summary>
    [HttpPut("{workingScheduleId:guid}/hours")]
    public async Task<ActionResult> SetWorkingHours(
        Guid barberProfileId,
        Guid salonId,
        Guid workingScheduleId,
        [FromBody] SetWorkingScheduleWorkingHoursRequest request,
        [FromServices] ICommandHandler<SetWorkingScheduleWorkingHoursCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SetWorkingScheduleWorkingHoursCommand(
                barberProfileId,
                salonId,
                workingScheduleId,
                request.StartTime,
                request.EndTime),
            cancellationToken));

    /// <summary>
    /// Помечает запись расписания выходным днём.
    /// </summary>
    [HttpPut("{workingScheduleId:guid}/day-off")]
    public async Task<ActionResult> SetDayOff(
        Guid barberProfileId,
        Guid salonId,
        Guid workingScheduleId,
        [FromServices] ICommandHandler<SetWorkingScheduleDayOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SetWorkingScheduleDayOffCommand(barberProfileId, salonId, workingScheduleId),
            cancellationToken));
}
