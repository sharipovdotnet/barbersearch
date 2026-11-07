using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Notifications.CreateTelegramLink;
using BarberSearch.Application.Notifications.GetBarberNotifications;
using BarberSearch.Application.Notifications.GetUnreadCount;
using BarberSearch.Application.Notifications.MarkAllNotificationsRead;
using BarberSearch.Application.Notifications.MarkNotificationRead;
using BarberSearch.Application.Notifications.UnlinkTelegram;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Инбокс напоминаний барбера и привязка Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Все маршруты — под политикой <c>BarberOnly</c>: аноним получает <c>401</c>, клиент и
/// администратор — <c>403</c>. Получатель всегда берётся из сессии, поэтому параметра
/// с идентификатором барбера нет и чужой инбокс запросить нечем.
/// </para>
/// <para>
/// Cookie-мутации (<c>POST</c>) проходят через глобальный antiforgery-фильтр: клиент обязан
/// прислать <c>X-CSRF-TOKEN</c>. Bearer-клиент ambient-учётных данных не имеет и фильтром
/// пропускается.
/// </para>
/// <para>
/// Маршрут <c>{id}</c> ограничен <c>{id:guid}</c>: иначе <c>read-all</c> и <c>unread-count</c>
/// были бы разобраны как идентификатор уведомления.
/// </para>
/// </remarks>
[ApiController]
[Route("api/barber/notifications")]
[Authorize(Policy = AuthorizationPolicies.BarberOnly)]
public sealed class BarberNotificationsController : ApiController
{
    /// <summary>
    /// Страница инбокса: свежие уведомления сверху, keyset-курсор.
    /// </summary>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BarberNotificationResponse>>> Get(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<BarberNotificationResponse>, GetBarberNotificationsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberNotificationsQuery(cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Отмечает уведомление прочитанным. Чужое уведомление — <c>404 notification.notFound</c>.
    /// </summary>
    /// <param name="id">Идентификатор уведомления.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(
        [FromRoute] Guid id,
        [FromServices] ICommandHandler<MarkNotificationReadCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new MarkNotificationReadCommand(id),
            cancellationToken));

    /// <summary>
    /// Отмечает все уведомления прочитанными. Идемпотентно.
    /// </summary>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(
        [FromServices] ICommandHandler<MarkAllNotificationsReadCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new MarkAllNotificationsReadCommand(),
            cancellationToken));

    /// <summary>
    /// Число непрочитанных уведомлений для бейджа колокольчика.
    /// </summary>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationsCountResponse>> GetUnreadCount(
        [FromServices] IQueryHandler<UnreadNotificationsCountResponse, GetUnreadCountQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetUnreadCountQuery(),
            cancellationToken));

    /// <summary>
    /// Выдаёт одноразовый код привязки и ссылку на бота.
    /// </summary>
    /// <remarks>
    /// Уже привязанный чат — <c>409 notification.telegram.alreadyLinked</c>, ненастроенный
    /// бот — <c>409 notification.telegram.notConfigured</c>. Повторная выдача кода заменяет
    /// прежний: в настройках хранится только последний хеш.
    /// </remarks>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("telegram/link")]
    public async Task<ActionResult<TelegramLinkResponse>> CreateTelegramLink(
        [FromServices] ICommandHandler<TelegramLinkResponse, CreateTelegramLinkCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new CreateTelegramLinkCommand(),
            cancellationToken));

    /// <summary>
    /// Отвязывает чат Telegram. Идемпотентно: отсутствие привязки — успех.
    /// </summary>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("telegram/unlink")]
    public async Task<IActionResult> UnlinkTelegram(
        [FromServices] ICommandHandler<UnlinkTelegramCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnlinkTelegramCommand(),
            cancellationToken));
}
