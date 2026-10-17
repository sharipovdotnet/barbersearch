using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings.AddBookingBarberNote;
using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.CompleteBooking;
using BarberSearch.Application.Bookings.ConfirmBooking;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Брони: создание и переходы статуса.
/// </summary>
/// <remarks>
/// Ключ идемпотентности приходит в теле создания: повтор запроса с тем же ключом возвращает
/// уже созданную бронь, а не вторую (см. AGENT.md §15.5).
/// </remarks>
[ApiController]
[Route("api/bookings")]
public class BookingsController : ApiController
{
    /// <summary>
    /// Создаёт бронь. Повтор с тем же ключом идемпотентности новой брони не создаёт.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateBookingRequest request,
        [FromServices] ICommandHandler<Guid, CreateBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(new CreateBookingCommand(request), cancellationToken));

    /// <summary>
    /// Подтверждает бронь.
    /// </summary>
    [HttpPost("{bookingId:guid}/confirmation")]
    public async Task<ActionResult> Confirm(
        Guid bookingId,
        [FromServices] ICommandHandler<ConfirmBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ConfirmBookingCommand(bookingId), cancellationToken));

    /// <summary>
    /// Завершает бронь.
    /// </summary>
    [HttpPost("{bookingId:guid}/completion")]
    public async Task<ActionResult> Complete(
        Guid bookingId,
        [FromServices] ICommandHandler<CompleteBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new CompleteBookingCommand(bookingId), cancellationToken));

    /// <summary>
    /// Отменяет бронь.
    /// </summary>
    [HttpPost("{bookingId:guid}/cancellation")]
    public async Task<ActionResult> Cancel(
        Guid bookingId,
        [FromServices] ICommandHandler<CancelBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new CancelBookingCommand(bookingId), cancellationToken));

    /// <summary>
    /// Помечает бронь как неявку. Слишком ранняя неявка — 400: правило Application.
    /// </summary>
    [HttpPost("{bookingId:guid}/no-show")]
    public async Task<ActionResult> MarkNoShow(
        Guid bookingId,
        [FromServices] ICommandHandler<MarkBookingNoShowCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new MarkBookingNoShowCommand(bookingId),
            cancellationToken));

    /// <summary>
    /// Добавляет заметку барбера к брони.
    /// </summary>
    [HttpPut("{bookingId:guid}/barber-note")]
    public async Task<ActionResult> AddBarberNote(
        Guid bookingId,
        [FromBody] AddBookingBarberNoteRequest request,
        [FromServices] ICommandHandler<AddBookingBarberNoteCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBookingBarberNoteCommand(bookingId, request.Note),
            cancellationToken));
}
