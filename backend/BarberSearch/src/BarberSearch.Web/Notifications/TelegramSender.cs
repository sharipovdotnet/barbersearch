using System.Globalization;
using System.Net;
using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Web.Notifications;

/// <summary>
/// Отправка сообщений в Telegram Bot API через <see cref="IHttpClientFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Адрес — <c>https://api.telegram.org/bot{token}/sendMessage</c>; пакет для Bot API не
/// нужен, запрос — обычная форма <c>chat_id</c> + <c>text</c>. Токен приходит из
/// <see cref="TelegramOptions"/> и никогда не попадает в лог, текст ошибки или ответ API.
/// </para>
/// <para>
/// Ошибки разделены так, как требует <see cref="ITelegramSender"/>: <c>400</c>/<c>403</c> —
/// чат недоступен (бот заблокирован, чат не найден), повторять бессмысленно; сеть,
/// таймаут, <c>429</c> и <c>5xx</c> — временная ошибка, повтор по расписанию.
/// В сообщение ошибки попадают только код состояния и стабильный публичный код — ни текст
/// сообщения, ни идентификатор чата, ни токен.
/// </para>
/// <para>
/// Без настроенного токена метод вообще не делает HTTP-запрос: воркер обязан работать без
/// Telegram (критерий приёмки 3), а не падать на пустом URL.
/// </para>
/// </remarks>
public sealed class TelegramSender : ITelegramSender
{
    /// <summary>
    /// Имя именованного <see cref="HttpClient"/> в <see cref="IHttpClientFactory"/>.
    /// </summary>
    public const string HTTP_CLIENT_NAME = "telegram";

    /// <summary>
    /// Таймаут одного запроса к Bot API.
    /// </summary>
    public static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);

    private const string API_ROOT = "https://api.telegram.org/bot";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramSender> _logger;

    public TelegramSender(
        IHttpClientFactory httpClientFactory,
        TelegramOptions options,
        ILogger<TelegramSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> SendMessageAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (!_options.IsConfigured)
        {
            // Токена нет — HTTP-запрос не выполняется. Ошибка временная с точки зрения
            // вызывающего: когда канал настроят, следующая попытка сработает.
            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Notification.TelegramSendFailed,
                "Telegram is not configured on the server.")));
        }

        // Токен уже проверен на пустоту (IsConfigured), поэтому склейка безопасна.
        var url = API_ROOT + _options.BotToken + "/sendMessage";

        try
        {
            var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);

            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>(
                    "chat_id",
                    chatId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("text", text)
            ]);

            using var response = await client.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return UnitResult.Success<Failure>();
            }

            var status = (int)response.StatusCode;

            // 400/403: бот заблокирован или чат не найден — повтор не поможет.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                return UnitResult.Failure(new Failure(Error.Failure(
                    ApiErrorCodes.Notification.TelegramChatUnavailable,
                    $"Telegram rejected the chat (HTTP {status}).")));
            }

            _logger.LogWarning(
                "Telegram sendMessage failed with HTTP {Status}.",
                status);

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Notification.TelegramSendFailed,
                $"Telegram send failed (HTTP {status}).")));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут HttpClient: вызывающий код не отменял запрос, значит это временная
            // ошибка канала, а не остановка воркера.
            _logger.LogWarning("Telegram sendMessage timed out.");

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Notification.TelegramSendFailed,
                "Telegram send timed out.")));
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Telegram is unreachable.");

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Notification.TelegramSendFailed,
                "Telegram is unreachable.")));
        }
    }
}
