using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Push.RegisterPushDevice;
using BarberSearch.Application.Push.UnregisterPushDevice;
using BarberSearch.Contracts.Push.RegisterPushDevice;
using BarberSearch.Contracts.Push.UnregisterPushDevice;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Регистрация и снятие устройства клиента для push-уведомлений.
/// </summary>
/// <remarks>
/// <para>
/// Маршрут закрыт fallback-политикой аутентификации: клиент, барбер и администратор могут
/// зарегистрировать своё устройство, аноним получает <c>401</c>. Роль не проверяется:
/// push о визите нужен любому аккаунту, у которого есть брони, а владелец устройства всегда
/// берётся из сессии — идентификатора пользователя в теле нет.
/// </para>
/// <para>
/// Оба действия отвечают <c>204</c> и идемпотентны. <c>POST api/push/devices</c> — upsert
/// по токену: повтор обновляет <c>last_seen_at</c>, токен из другого аккаунта переходит
/// к текущему. <c>POST api/push/devices/unregister</c> — удаление только своего токена;
/// неизвестный или чужой токен тоже <c>204</c>, чтобы ответ не работал oracle'ом
/// «зарегистрирован ли этот токен у другого аккаунта».
/// </para>
/// <para>
/// Токен передаётся в теле, а не в пути: это длинная строка устройства, и ей не место
/// в URL, журналах прокси и истории. Bearer-клиент не проходит CSRF-фильтр, поэтому
/// мобильному приложению дополнительный заголовок не нужен; лимит записи <c>30/мин</c>
/// на <c>sub</c> действует как на любой <c>POST</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/push/devices")]
public sealed class PushDevicesController : ApiController
{
    /// <summary>
    /// Регистрирует токен устройства текущего пользователя.
    /// </summary>
    /// <remarks>
    /// Ошибки полей — <c>400 pushDeviceToken.token.invalid</c> /
    /// <c>pushDeviceToken.token.tooLong</c> / <c>pushDeviceToken.platform.unknown</c>
    /// с <c>invalidField</c>. Платформа передаётся именем (<c>"Android"</c>), как остальные
    /// перечисления в API.
    /// </remarks>
    /// <param name="request">Токен устройства и платформа.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost]
    public async Task<ActionResult> Register(
        [FromBody] RegisterPushDeviceRequest request,
        [FromServices] ICommandHandler<RegisterPushDeviceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RegisterPushDeviceCommand(request),
            cancellationToken));

    /// <summary>
    /// Снимает токен устройства текущего пользователя.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: неизвестный, чужой и пустой токен — успех без эффекта. Это осознанно:
    /// ответ не должен подсказывать, существует ли токен у другого аккаунта.
    /// </remarks>
    /// <param name="request">Токен устройства.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("unregister")]
    public async Task<ActionResult> Unregister(
        [FromBody] UnregisterPushDeviceRequest request,
        [FromServices] ICommandHandler<UnregisterPushDeviceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnregisterPushDeviceCommand(request),
            cancellationToken));
}
