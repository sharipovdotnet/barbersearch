using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberSalons.AddBarberSalonDayOff;
using BarberSearch.Application.BarberSalons.AddBarberSalonWorkingSchedule;
using BarberSearch.Application.BarberSalons.RemoveBarberSalonWorkingSchedule;
using BarberSearch.Contracts.BarberSalons.AddBarberSalonWorkingSchedule;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Салоны барбера: рабочие дни и выходные в расписании пары (барбер, салон).
/// </summary>
/// <remarks>
/// Расписание принадлежит паре (барбер, салон), поэтому адрес идёт через профиль барбера
/// и салон. Правка существующей записи — в <see cref="WorkingSchedulesController"/>.
/// </remarks>
[ApiController]
[Route("api/barber-profiles/{barberProfileId:guid}/salons/{salonId:guid}")]
public class BarberSalonsController : ApiController
{
    /// <summary>
    /// Добавляет рабочий день в расписание. Дубликат по дню недели — 409.
    /// </summary>
    [HttpPost("working-schedules")]
    public async Task<ActionResult> AddWorkingSchedule(
        Guid barberProfileId,
        Guid salonId,
        [FromBody] AddBarberSalonWorkingScheduleRequest request,
        [FromServices] ICommandHandler<AddBarberSalonWorkingScheduleCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberSalonWorkingScheduleCommand(
                barberProfileId,
                salonId,
                request.DayOfWeek,
                request.StartTime,
                request.EndTime),
            cancellationToken));

    /// <summary>
    /// Удаляет запись расписания из салона барбера.
    /// </summary>
    [HttpDelete("working-schedules/{workingScheduleId:guid}")]
    public async Task<ActionResult> RemoveWorkingSchedule(
        Guid barberProfileId,
        Guid salonId,
        Guid workingScheduleId,
        [FromServices] ICommandHandler<RemoveBarberSalonWorkingScheduleCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberSalonWorkingScheduleCommand(barberProfileId, salonId, workingScheduleId),
            cancellationToken));

    /// <summary>
    /// Помечает день недели нерабочим. Время для выходного не задаётся.
    /// </summary>
    [HttpPost("day-offs/{dayOfWeek}")]
    public async Task<ActionResult> AddDayOff(
        Guid barberProfileId,
        Guid salonId,
        DayOfWeek dayOfWeek,
        [FromServices] ICommandHandler<AddBarberSalonDayOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberSalonDayOffCommand(barberProfileId, salonId, dayOfWeek),
            cancellationToken));
}
