using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Infrastructure.Push;

/// <summary>
/// Отправка push-сообщений в FCM HTTP v1 через <see cref="IHttpClientFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Пакет для FCM не нужен: адрес — <c>https://fcm.googleapis.com/v1/projects/{projectId}/messages:send</c>,
/// тело — стандартный <c>{"message":{…}}</c>, авторизация — <c>Bearer</c> из
/// <see cref="FcmAccessTokenProvider"/>. Сообщение содержит <c>notification</c> (заголовок и
/// текст), <c>data</c> (<c>type</c> из <see cref="PushMessage.Type"/> и <c>bookingId</c>) и
/// <c>android.priority=high</c> плюс <c>android.notification.channel_id="reminders"</c> —
/// канал, который создаёт мобильное приложение; без него Android 8+ молча отбрасывает
/// уведомление. Канал общий для напоминаний и событий создания: это канал доставки, а не
/// тип сообщения.
/// </para>
/// <para>
/// Ошибки разделены так, как требует <see cref="IPushSender"/>: мёртвый токен — только
/// разобранный <c>UNREGISTERED</c> или документированное сообщение о недействительном
/// registration token (строка удаляется, повтор не нужен); <c>401/403</c> — проблема
/// с credentials, причём access-токен сбрасывается и запрос повторяется один раз;
/// сеть, таймаут, <c>429</c> и <c>5xx</c> — временная ошибка. В сообщение ошибки попадают
/// только код состояния и стабильный публичный код — ни токен, ни заголовок, ни текст
/// сообщения.
/// </para>
/// <para>
/// Без настроенного канала метод не делает HTTP-запрос: воркер обязан работать без
/// credentials (критерий приёмки), а не падать на пустом project id.
/// </para>
/// </remarks>
public sealed class FcmPushSender : IPushSender
{
    /// <summary>
    /// Имя именованного <see cref="HttpClient"/> в <see cref="IHttpClientFactory"/>.
    /// </summary>
    public const string HTTP_CLIENT_NAME = "fcm";

    /// <summary>
    /// Таймаут одного запроса к FCM или OAuth.
    /// </summary>
    public static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Корень FCM HTTP v1.
    /// </summary>
    private const string API_ROOT = "https://fcm.googleapis.com/v1/projects";

    /// <summary>
    /// Канал Android, который создаёт мобильное приложение; без него уведомление не покажется.
    /// </summary>
    public const string ANDROID_CHANNEL_ID = "reminders";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PushOptions _options;
    private readonly FcmAccessTokenProvider _accessTokenProvider;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(
        IHttpClientFactory httpClientFactory,
        PushOptions options,
        FcmAccessTokenProvider accessTokenProvider,
        ILogger<FcmPushSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _accessTokenProvider = accessTokenProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> SendAsync(
        PushMessage message,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (!_options.IsConfigured)
        {
            // Ключей нет — HTTP-запрос не выполняется. Ошибка временная: когда канал
            // настроят, следующая попытка сработает. В обычном режиме сюда не попадаем:
            // обработчик доставки сам обрабатывает «не настроено» как dev mode.
            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Unavailable,
                "Push is not configured on the server.")));
        }

        var accessToken = await _accessTokenProvider
            .GetAccessTokenAsync(cancellationToken)
            .ConfigureAwait(false);
        if (accessToken.IsFailure)
        {
            return accessToken.Error;
        }

        var url = $"{API_ROOT}/{_options.ProjectId!.Trim()}/messages:send";
        var payload = BuildPayload(message, token);

        var first = await SendRequestAsync(
            url,
            payload,
            accessToken.Value,
            cancellationToken)
            .ConfigureAwait(false);

        if (!first.Unauthorized)
        {
            return first.Result;
        }

        // 401/403 — отвергнут именно access-токен. Сбрасываем кэш и пробуем ещё раз с новым:
        // иначе все повторы до истечения часа будут отправлять тот же отвергнутый токен.
        _accessTokenProvider.Invalidate(accessToken.Value);

        var refreshed = await _accessTokenProvider
            .GetAccessTokenAsync(cancellationToken)
            .ConfigureAwait(false);
        if (refreshed.IsFailure)
        {
            return refreshed.Error;
        }

        var second = await SendRequestAsync(
            url,
            payload,
            refreshed.Value,
            cancellationToken)
            .ConfigureAwait(false);

        return second.Result;
    }

    /// <summary>
    /// Один HTTP-запрос к FCM и перевод ответа; <see cref="SendOutcome.Unauthorized"/>
    /// говорит вызывающему, что можно один раз обновить access-токен.
    /// </summary>
    private async Task<SendOutcome> SendRequestAsync(
        string url,
        string payload,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new SendOutcome(false, UnitResult.Success<Failure>());
            }

            var status = (int)response.StatusCode;
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var unauthorized = response.StatusCode
                is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

            return new SendOutcome(
                unauthorized,
                MapError(response.StatusCode, status, body));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут HttpClient: вызывающий не отменял запрос, значит это временная
            // ошибка канала, а не остановка воркера.
            _logger.LogWarning("Push send timed out.");

            return new SendOutcome(false, UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push send timed out."))));
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Push endpoint is unreachable.");

            return new SendOutcome(false, UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push endpoint is unreachable."))));
        }
    }

    /// <summary>
    /// Собирает тело запроса FCM HTTP v1.
    /// </summary>
    /// <remarks>
    /// Значения <c>data</c> — строки, как требует FCM; <c>bookingId</c> — канонический
    /// <c>D</c>-формат GUID, чтобы клиент разобрал его без вариантов. Ничего, кроме типа
    /// и идентификатора визита, в data не кладётся.
    /// </remarks>
    internal static string BuildPayload(PushMessage message, string token)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var payload = new
        {
            message = new
            {
                token,
                notification = new
                {
                    title = message.Title,
                    body = message.Body
                },
                data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["type"] = message.Type,
                    ["bookingId"] = message.BookingId.ToString("D")
                },
                android = new
                {
                    priority = "high",
                    notification = new
                    {
                        channel_id = ANDROID_CHANNEL_ID
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Переводит ответ FCM в код ошибки <see cref="IPushSender"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Мёртвый токен распознаётся не по одному коду состояния: FCM отдаёт <c>404</c> и на
    /// <c>UNREGISTERED</c>, и на неверный проект, а <c>400 INVALID_ARGUMENT</c> — и на
    /// недействительный registration token, и на кривое тело запроса. Удалять устройства
    /// по ошибке конфигурации нельзя, поэтому решает разобранное тело
    /// (<see cref="IsUnregisteredError"/>): <c>error.status = UNREGISTERED</c>,
    /// <c>error.details[].errorCode = UNREGISTERED</c> или документированное сообщение
    /// о недействительном registration token.
    /// </para>
    /// <para>
    /// Всё остальное: <c>404</c> — неверный проект/эндпоинт (<c>push.unavailable</c>),
    /// прочие <c>400</c> — ошибка запроса (<c>push.send</c>, повторяется), <c>401/403</c> —
    /// credentials, <c>429/5xx</c> — временная ошибка. Тело не логируется: в нём бывает
    /// эхо запроса.
    /// </para>
    /// </remarks>
    private UnitResult<Failure> MapError(HttpStatusCode statusCode, int status, string body)
    {
        if (IsUnregisteredError(body))
        {
            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.TokenUnregistered,
                "The FCM registration token is not registered.")));
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Push endpoint or project was not found (HTTP 404).");

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Unavailable,
                "Push project or endpoint was not found (HTTP 404).")));
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("Push send was rejected with HTTP 400.");

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push request was rejected (HTTP 400).")));
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Push credentials were rejected with HTTP {Status}.",
                status);

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.Push.Unavailable,
                $"Push credentials were rejected (HTTP {status}).")));
        }

        _logger.LogWarning("Push send failed with HTTP {Status}.", status);

        return UnitResult.Failure(new Failure(Error.Failure(
            ApiErrorCodes.Push.Send,
            $"Push send failed (HTTP {status}).")));
    }

    /// <summary>
    /// Разбирает тело ошибки FCM и отвечает, означает ли она мёртвый registration token.
    /// </summary>
    /// <remarks>
    /// Используются три документированных признака: <c>error.status = UNREGISTERED</c>,
    /// элемент <c>error.details[].errorCode = UNREGISTERED</c> и сообщение
    /// «registration token is not a valid FCM registration token». Простое наличие строки
    /// <c>INVALID_ARGUMENT</c> в теле — недостаточно: этот статус приходит и на malformed
    /// payload, и на неверный проект.
    /// </remarks>
    private static bool IsUnregisteredError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (error.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "UNREGISTERED", StringComparison.Ordinal))
            {
                return true;
            }

            if (error.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.ValueKind == JsonValueKind.Object
                        && detail.TryGetProperty("errorCode", out var errorCode)
                        && errorCode.ValueKind == JsonValueKind.String
                        && string.Equals(
                            errorCode.GetString(),
                            "UNREGISTERED",
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return error.TryGetProperty("message", out var message)
                   && message.ValueKind == JsonValueKind.String
                   && IsUnregisteredMessage(message.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Документированные сообщения FCM о недействительном registration token.
    /// </summary>
    private static bool IsUnregisteredMessage(string? message)
        => message is not null
           && (message.Contains(
                   "not a valid FCM registration token",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "registration token is not valid",
                   StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Итог одного HTTP-запроса: результат и признак «отвергнут access-токен».
    /// </summary>
    private readonly record struct SendOutcome(bool Unauthorized, UnitResult<Failure> Result);
}
