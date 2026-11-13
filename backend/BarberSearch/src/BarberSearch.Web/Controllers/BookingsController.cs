using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings.AddBookingBarberNote;
using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.ChangeBookingPrice;
using BarberSearch.Application.Bookings.CompleteBooking;
using BarberSearch.Application.Bookings.ConfirmBooking;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.Bookings.GetBookingRescheduleSlots;
using BarberSearch.Application.Bookings.GetBookings;
using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Application.Bookings.RescheduleBooking;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.CancelBooking;
using BarberSearch.Contracts.Bookings.ChangeBookingPrice;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    /// Отменяет бронь. Барбер обязан передать причину; клиент и администратор — нет.
    /// </summary>
    /// <remarks>
    /// Тело необязательно, чтобы существующие вызовы без него продолжали работать:
    /// причина нужна только ветке барбера, и её отсутствие обрабатывает Application.
    /// </remarks>
    [HttpPost("{bookingId:guid}/cancellation")]
    public async Task<ActionResult> Cancel(
        Guid bookingId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelBookingRequest? request,
        [FromServices] ICommandHandler<CancelBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new CancelBookingCommand(bookingId, request?.Reason),
            cancellationToken));

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
    /// Переносит визит на другое время: меняются только начало и окончание.
    /// </summary>
    /// <remarks>
    /// Ответ — <c>204</c> без тела, как у остальных переходов брони: новое время клиент уже
    /// знает из слота, а список перечитывается опросом. Услуга, салон, барбер, цена и статус
    /// не меняются, длительность сохраняется по самой брони. Новое время обязано быть стартом
    /// слота из <see cref="GetRescheduleSlots"/>; повтор с тем же временем — безвредный no-op.
    /// </remarks>
    /// <param name="bookingId">Идентификатор переносимой брони.</param>
    /// <param name="request">Новое начало визита.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{bookingId:guid}/reschedule")]
    public async Task<ActionResult> Reschedule(
        Guid bookingId,
        [FromBody] RescheduleBookingRequest request,
        [FromServices] ICommandHandler<RescheduleBookingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RescheduleBookingCommand(bookingId, request.StartAt),
            cancellationToken));

    /// <summary>
    /// Возвращает слоты для переноса визита на дату.
    /// </summary>
    /// <remarks>
    /// Отдельное чтение, а не параметр публичных слотов: длительность и салон берутся из самой
    /// брони, поэтому она могла разойтись с текущим прайсом. Контракт — обычный
    /// <c>BarberSlotsResponse</c>; своя бронь слот не занимает, чужие — занимают.
    /// </remarks>
    /// <param name="bookingId">Идентификатор переносимой брони.</param>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{bookingId:guid}/reschedule-slots")]
    public async Task<ActionResult<BarberSlotsResponse>> GetRescheduleSlots(
        Guid bookingId,
        [FromQuery] DateOnly? date,
        [FromServices] IQueryHandler<BarberSlotsResponse, GetBookingRescheduleSlotsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBookingRescheduleSlotsQuery(bookingId, date),
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

    /// <summary>
    /// Меняет цену визита.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Менять сумму может владелец профиля барбера и администратор — одна политика на оба
    /// кабинета, как у отмены и неявки; клиент получает <c>403</c>, аноним — <c>401</c>.
    /// Меняется только сумма: услуга, салон, барбер, время и статус остаются прежними.
    /// </para>
    /// <para>
    /// Цену правят у <c>Pending</c>, <c>Confirmed</c> и <c>Completed</c>; у отменённой записи
    /// и у неявки — <c>409 booking.price.status</c>. Отсутствующее поле в теле даёт
    /// <c>400 booking.price.required</c> (тело принимает <c>decimal?</c>: <c>0</c> — валидная
    /// цена и не может быть значением по умолчанию), отрицательная сумма, больше
    /// <c>99 999 999.99</c> или с копейками дробнее двух знаков — <c>400</c>.
    /// </para>
    /// <para>
    /// Успех — <c>204</c> без тела. Каждая реальная правка пишет строку <c>booking.updated</c>
    /// с <c>changes.price.from/to</c>; повтор с той же суммой — успех без строки журнала
    /// и без сдвига <c>updated_at</c>.
    /// </para>
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="request">Новая цена визита.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPut("{bookingId:guid}/price")]
    public async Task<ActionResult> ChangePrice(
        Guid bookingId,
        [FromBody] ChangeBookingPriceRequest request,
        [FromServices] ICommandHandler<ChangeBookingPriceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ChangeBookingPriceCommand(bookingId, request.Price),
            cancellationToken));
}
