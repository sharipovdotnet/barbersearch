using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Notifications.GetNotificationSettings;
using BarberSearch.Application.Notifications.UpdateNotificationSettings;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Настройки напоминаний барбера: чтение и полная замена.
/// </summary>
/// <remarks>
/// <para>
/// Маршрут закрыт политикой <c>BarberOnly</c>; владелец настроек — текущий пользователь
/// из сессии, поэтому идентификатора в пути нет. Первое чтение создаёт настройки по
/// умолчанию, так что <c>404</c> здесь не бывает.
/// </para>
/// <para>
/// <c>PUT</c> возвращает <b>200 с полным</b> <see cref="NotificationSettingsResponse"/>,
/// а не <c>204</c>: клиент показывает нормализованные сервером значения (например,
/// <c>digestTime</c> в формате <c>HH:mm</c>) и не должен перечитывать настройки после
/// каждого изменения. Это cookie-мутация, поэтому нужен <c>X-CSRF-TOKEN</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/barber/notification-settings")]
[Authorize(Policy = AuthorizationPolicies.BarberOnly)]
public sealed class BarberNotificationSettingsController : ApiController
{
    /// <summary>
    /// Возвращает настройки текущего барбера, создавая их при первом чтении.
    /// </summary>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<NotificationSettingsResponse>> Get(
        [FromServices] IQueryHandler<NotificationSettingsResponse, GetNotificationSettingsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetNotificationSettingsQuery(),
            cancellationToken));

    /// <summary>
    /// Заменяет настройки целиком и возвращает сохранённое состояние.
    /// </summary>
    /// <remarks>
    /// Ошибки полей — <c>400 notificationSettings.leadMinutes</c> /
    /// <c>notificationSettings.digestTime</c>; <c>digestTime</c> принимается строкой
    /// <c>HH:mm</c>, как его отдаёт <c>GET</c> и принимает <c>&lt;input type="time"&gt;</c>.
    /// </remarks>
    /// <param name="request">Новое состояние настроек.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPut]
    public async Task<ActionResult<NotificationSettingsResponse>> Update(
        [FromBody] UpdateNotificationSettingsRequest request,
        [FromServices] ICommandHandler<NotificationSettingsResponse, UpdateNotificationSettingsCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateNotificationSettingsCommand(request),
            cancellationToken));
}
