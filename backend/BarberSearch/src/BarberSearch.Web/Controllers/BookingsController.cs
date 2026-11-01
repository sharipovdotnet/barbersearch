using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings.AddBookingBarberNote;
using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.CompleteBooking;
using BarberSearch.Application.Bookings.ConfirmBooking;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.Bookings.GetBookings;
using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
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
    /// Возвращает брони: свои для клиента, все для администратора.
    /// </summary>
    /// <remarks>
    /// Обычный пользователь не может запросить чужой список — это 403. Администратор
    /// может сузить выдачу параметром <paramref name="userId"/>; несуществующий клиент
    /// даёт 404. <paramref name="scope"/> делит список на вкладки: <c>all</c> по умолчанию
    /// (прежний порядок, свежие сверху), <c>upcoming</c> — активные брони с
    /// <c>EndAt &gt; now</c> по возрастанию, <c>past</c> — остальные по убыванию.
    /// Пагинация курсорная; направление курсора задаёт скоуп.
    /// </remarks>
    /// <param name="userId">Клиент для администратора; остальным доступен только свой id.</param>
    /// <param name="scope">Скоуп выдачи: <c>all</c>, <c>upcoming</c> или <c>past</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BookingResponse>>> Get(
        [FromQuery] Guid? userId,
        [FromQuery] string? scope,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<BookingResponse>, GetBookingsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBookingsQuery(userId, scope, cursor, pageSize),
            cancellationToken));

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
