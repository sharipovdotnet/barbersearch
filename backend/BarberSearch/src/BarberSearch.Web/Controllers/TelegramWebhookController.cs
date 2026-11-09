using System.Security.Cryptography;
using System.Text;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.LinkTelegramChat;
using BarberSearch.Infrastructure.Telegram;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Вебхук Telegram Bot API: привязка чата по одноразовому коду.
/// </summary>
/// <remarks>
/// <para>
/// Эндпоинт анонимный и без antiforgery: у Telegram нет ни cookie, ни CSRF-токена.
/// Единственная защита — заголовок <c>X-Telegram-Bot-Api-Secret-Token</c>, который сверяется
/// с <c>Telegram:WebhookSecret</c> за постоянное время; неверный секрет — <c>403</c>.
/// </para>
/// <para>
/// При верном секрете ответ всегда <c>200</c>, даже если код истёк, чат уже привязан или
/// привязка не удалась: Telegram ретраит не-<c>2xx</c>, и повтор не должен менять состояние.
/// Идемпотентность по <c>update_id</c> держит таблица <c>telegram_updates</c>, поэтому один
/// и тот же апдейт можно принять сколько угодно раз.
/// </para>
/// <para>
/// Эндпоинт исключён из глобального лимита записи (<see cref="SkipRateLimitAttribute"/>):
/// запросы приходят с адресов Telegram, и <c>429</c> означал бы потерянное обновление.
/// </para>
/// </remarks>
[ApiController]
[Route("api/telegram/webhook")]
[AllowAnonymous]
public sealed class TelegramWebhookController : ControllerBase
{
    /// <summary>
    /// Заголовок, которым Telegram подтверждает подлинность вебхука.
    /// </summary>
    public const string SECRET_HEADER_NAME = "X-Telegram-Bot-Api-Secret-Token";

    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        TelegramOptions telegramOptions,
        ILogger<TelegramWebhookController> logger)
    {
        ArgumentNullException.ThrowIfNull(telegramOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _telegramOptions = telegramOptions;
        _logger = logger;
    }

    /// <summary>
    /// Принимает обновление и, если это <c>/start {code}</c>, привязывает чат.
    /// </summary>
    /// <param name="update">Обновление Bot API; пустое тело допускается.</param>
    /// <param name="handler">Обработчик привязки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost]
    [SkipCookieAntiforgery]
    [SkipRateLimit]
    public async Task<IActionResult> Receive(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TelegramWebhookUpdate? update,
        [FromServices] ICommandHandler<LinkTelegramChatCommand> handler,
        CancellationToken cancellationToken)
    {
        if (!IsSecretValid(
                _telegramOptions.WebhookSecret,
                Request.Headers[SECRET_HEADER_NAME].ToString()))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (update?.Message?.Chat is not { } chat)
        {
            // Обновление без сообщения (например, edited_message) нам не интересно,
            // но Telegram всё равно должен получить 200.
            return Ok();
        }

        var command = new LinkTelegramChatCommand(
            update.UpdateId,
            chat.Id,
            ExtractStartCode(update.Message.Text));

        var result = await handler.Handle(command, cancellationToken);

        if (result.IsFailure)
        {
            // Тело ответа Telegram не читает, поэтому ошибка только в журнале и без ПДн:
            // коды стабильны, а текст сообщения и чат в лог не попадают.
            _logger.LogWarning(
                "Telegram webhook update {UpdateId} failed: {Codes}.",
                update.UpdateId,
                Describe(result.Error));
        }

        return Ok();
    }

    /// <summary>
    /// Достаёт код из текста <c>/start {code}</c> (в том числе <c>/start@Bot {code}</c>).
    /// </summary>
    /// <remarks>
    /// Возвращается <c>null</c>, если сообщение — не <c>/start</c>: на обычные сообщения бот
    /// не отвечает. Код приходит сырым; нормализует его <see cref="TelegramLinkCode"/>.
    /// </remarks>
    internal static string? ExtractStartCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        var command = parts[0];

        var isStart = command.Equals("/start", StringComparison.OrdinalIgnoreCase)
                      || command.StartsWith("/start@", StringComparison.OrdinalIgnoreCase);

        if (!isStart || parts.Length < 2)
        {
            return null;
        }

        return parts[1];
    }

    /// <summary>
    /// Сравнивает секрет заголовка с настроенным за постоянное время.
    /// </summary>
    /// <remarks>
    /// Пустой настроенный секрет означает, что вебхук не настроен: принимать запросы
    /// с пустым заголовком нельзя. Длина сравнивается отдельно — <c>FixedTimeEquals</c>
    /// требует одинаковых массивов, а разные длины и так выдают факт несовпадения.
    /// </remarks>
    internal static bool IsSecretValid(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    /// <summary>
    /// Строка кодов ошибки для журнала.
    /// </summary>
    private static string Describe(Failure failure)
        => failure.Errors.Count == 0
            ? "unknown"
            : string.Join(", ", failure.Errors.Select(error => error.Code));
}
