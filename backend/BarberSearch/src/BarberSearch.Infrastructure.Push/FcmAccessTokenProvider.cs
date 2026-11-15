using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Infrastructure.Push;

/// <summary>
/// OAuth-токен сервисного аккаунта Google для FCM HTTP v1: подпись JWT и кэш access-токена.
/// </summary>
/// <remarks>
/// <para>
/// Пакет для OAuth не нужен: RS256-подпись собирается <see cref="RSA"/> и
/// <c>System.Text.Json</c>, а запрос — обычная форма <c>grant_type=…&amp;assertion=…</c> на
/// <c>token_uri</c> ключа. Токен живёт час; кэшируем его до <c>expires_in − 60 с</c>, чтобы
/// тик из двадцати сообщений не делал двадцать запросов за токеном.
/// </para>
/// <para>
/// Одновременные тики не должны подписывать JWT параллельно: доступ к кэшу закрыт
/// <see cref="SemaphoreSlim"/>. Повторная проверка под замком нужна, потому что второй
/// вызывающий мог дождаться, пока первый уже положил токен.
/// </para>
/// <para>
/// Секреты не логируются: ни PEM, ни access-токен, ни <c>assertion</c> не попадают ни
/// в сообщение ошибки, ни в журнал. Наружу — только код ошибки.
/// </para>
/// </remarks>
public sealed class FcmAccessTokenProvider : IDisposable
{
    /// <summary>
    /// Область доступа FCM: ровно она требуется сервисному аккаунту для <c>messages:send</c>.
    /// </summary>
    public const string SCOPE = "https://www.googleapis.com/auth/firebase.messaging";

    /// <summary>
    /// Срок жизни подписанного JWT: час, как требует Google.
    /// </summary>
    internal static readonly TimeSpan ASSERTION_LIFETIME = TimeSpan.FromHours(1);

    /// <summary>
    /// Запас, на который access-токен перестаёт считаться живым раньше срока.
    /// </summary>
    internal static readonly TimeSpan EXPIRY_SKEW = TimeSpan.FromSeconds(60);

    private const string JWT_BEARER_GRANT_TYPE =
        "urn:ietf:params:oauth:grant-type:jwt-bearer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PushOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FcmAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt;
    private bool _signingKeyFailureLogged;

    public FcmAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        PushOptions options,
        TimeProvider timeProvider,
        ILogger<FcmAccessTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Освобождает семафор кэша. Провайдер — синглтон, поэтому вызывается при остановке хоста.
    /// </summary>
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Возвращает access-токен из кэша или запрашивает новый.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Access-токен либо <c>push.unavailable</c>/<c>push.send</c>.</returns>
    public async Task<Result<string, Failure>> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        if (_accessToken is not null && now < _expiresAt)
        {
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            now = _timeProvider.GetUtcNow();

            if (_accessToken is not null && now < _expiresAt)
            {
                return _accessToken;
            }

            var key = _options.ServiceAccount;
            if (key is null)
            {
                return new Failure(Error.Failure(
                    ApiErrorCodes.Push.Unavailable,
                    "Push is not configured on the server."));
            }

            string assertion;

            try
            {
                assertion = BuildAssertion(key, now);
            }
            catch (CryptographicException)
            {
                return SigningKeyFailure();
            }
            catch (ArgumentException)
            {
                // ImportFromPem бросает ArgumentException/FormatException на строке, которая
                // прошла проверку «BEGIN PRIVATE KEY», но не является валидным PEM. Это ошибка
                // конфигурации, а не повод уронить весь шаг доставки.
                return SigningKeyFailure();
            }
            catch (FormatException)
            {
                return SigningKeyFailure();
            }

            return await RequestTokenAsync(key.TokenUri, assertion, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Сбрасывает закэшированный access-токен после <c>401/403</c>, чтобы повтор шёл с новым.
    /// </summary>
    /// <remarks>
    /// Токен сбрасывается только если это тот самый токен, который отверг FCM: параллельный
    /// поток мог уже получить свежий, и его нельзя выкидывать. Повтор запроса делает
    /// <see cref="FcmPushSender"/> ровно один раз.
    /// </remarks>
    /// <param name="rejectedAccessToken">Отвергнутый access-токен или <c>null</c> — сбросить любой.</param>
    public void Invalidate(string? rejectedAccessToken)
    {
        _gate.Wait();

        try
        {
            if (rejectedAccessToken is null
                || string.Equals(_accessToken, rejectedAccessToken, StringComparison.Ordinal))
            {
                _accessToken = null;
                _expiresAt = default;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ошибка «ключ разобран, но не годится для подписи»: предупреждение пишется один раз.
    /// </summary>
    private Failure SigningKeyFailure()
    {
        if (!_signingKeyFailureLogged)
        {
            _signingKeyFailureLogged = true;

            _logger.LogWarning("Push service account key cannot be used for signing.");
        }

        return new Failure(Error.Failure(
            ApiErrorCodes.Push.Unavailable,
            "Push service account key is not usable."));
    }

    /// <summary>
    /// Собирает подписанный RS256 JWT сервисного аккаунта.
    /// </summary>
    /// <remarks>
    /// <c>aud</c> — <c>token_uri</c> ключа, <c>iss</c> — <c>client_email</c>, <c>scope</c> —
    /// <see cref="SCOPE"/>. Это соответствует документации Google «OAuth 2.0 for service
    /// accounts»; пакета для JWT нет, поэтому заголовок и полезная нагрузка собираются
    /// обычным JSON.
    /// </remarks>
    internal static string BuildAssertion(ServiceAccountKey key, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);

        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            typ = "JWT"
        }));

        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = key.ClientEmail,
            scope = SCOPE,
            aud = key.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(ASSERTION_LIFETIME).ToUnixTimeSeconds()
        }));

        var signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKeyPem);

        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    /// <summary>
    /// Обменивает подписанный JWT на access-токен и кэширует его.
    /// </summary>
    private async Task<Result<string, Failure>> RequestTokenAsync(
        string tokenUri,
        string assertion,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(FcmPushSender.HTTP_CLIENT_NAME);

            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", JWT_BEARER_GRANT_TYPE),
                new KeyValuePair<string, string>("assertion", assertion)
            ]);

            using var response = await client
                .PostAsync(tokenUri, content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                _logger.LogWarning(
                    "Push OAuth token request failed with HTTP {Status}.",
                    status);

                return new Failure(Error.Failure(
                    ApiErrorCodes.Push.Unavailable,
                    $"Push OAuth token request failed (HTTP {status})."));
            }

            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!TryReadAccessToken(body, out var accessToken, out var expiresInSeconds))
            {
                _logger.LogWarning("Push OAuth token response has no usable access token.");

                return new Failure(Error.Failure(
                    ApiErrorCodes.Push.Unavailable,
                    "Push OAuth token response is invalid."));
            }

            var lifetime = TimeSpan.FromSeconds(expiresInSeconds) - EXPIRY_SKEW;

            _accessToken = accessToken;
            _expiresAt = lifetime > TimeSpan.Zero
                ? _timeProvider.GetUtcNow().Add(lifetime)
                : _timeProvider.GetUtcNow();

            return accessToken;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Push OAuth token request timed out.");

            return new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push OAuth token request timed out."));
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Push OAuth endpoint is unreachable.");

            return new Failure(Error.Failure(
                ApiErrorCodes.Push.Send,
                "Push OAuth endpoint is unreachable."));
        }
    }

    /// <summary>
    /// Читает <c>access_token</c> и <c>expires_in</c> из ответа OAuth.
    /// </summary>
    /// <remarks>
    /// <c>expires_in</c> приходит числом, но допускаем и строку: формат ответа Google
    /// стабилен, однако падать на одном поле из-за типа не стоит. Отсутствие
    /// <c>expires_in</c> трактуется как час.
    /// </remarks>
    private static bool TryReadAccessToken(
        string body,
        out string accessToken,
        out int expiresInSeconds)
    {
        accessToken = string.Empty;
        expiresInSeconds = (int)ASSERTION_LIFETIME.TotalSeconds;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || tokenElement.GetString() is not { Length: > 0 } token)
            {
                return false;
            }

            accessToken = token;

            if (root.TryGetProperty("expires_in", out var expiresElement))
            {
                expiresInSeconds = expiresElement.ValueKind switch
                {
                    JsonValueKind.Number when expiresElement.TryGetInt32(out var number) => number,
                    JsonValueKind.String when int.TryParse(
                        expiresElement.GetString(),
                        out var parsed) => parsed,
                    _ => expiresInSeconds
                };
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Кодирует байты в base64url без выравнивающих <c>=</c>.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
