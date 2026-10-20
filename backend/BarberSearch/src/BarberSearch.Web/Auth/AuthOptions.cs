using BarberSearch.Domain.RefreshTokens;
using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Настройки аутентификации из секции <c>Auth</c>.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> в репозитории не хранится и не генерируется приложением:
/// он обязан прийти из секрет-стора или переменной окружения <c>Auth__SigningKey</c>
/// и быть не короче 32 символов. Приложение падает на старте, если ключа нет.
/// <para>
/// <see cref="PreviousSigningKeys"/> нужен для ротации: текущий ключ подписывает новые токены,
/// предыдущие продолжают проверяться, пока живут выданные ими access-токены. Пустой список
/// означает, что ротация не настроена.
/// </para>
/// </remarks>
public sealed record AuthOptions
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Минимальная длина ключа подписи в символах.
    /// </summary>
    public const int MIN_SIGNING_KEY_LENGTH = 32;

    /// <summary>
    /// Издатель токена (<c>iss</c>).
    /// </summary>
    public string Issuer { get; init; } = "BarberSearch";

    /// <summary>
    /// Получатель токена (<c>aud</c>) — идентификатор этого API.
    /// </summary>
    public string Audience { get; init; } = "BarberSearch.Api";

    /// <summary>
    /// Текущий ключ подписи HMAC-SHA256. Не хранится в репозитории.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Предыдущие ключи подписи, которые ещё принимаются при проверке токенов.
    /// </summary>
    public string[] PreviousSigningKeys { get; init; } = [];

    /// <summary>
    /// Срок жизни access-токена в минутах.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>
    /// Срок жизни refresh-токена в днях. Допустимо 7–30: короткий срок заставляет клиента
    /// часто обновляться, длинный — растягивает окно украденного токена.
    /// </summary>
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>
    /// Срок жизни постоянной cookie в часах.
    /// </summary>
    public int CookieHours { get; init; } = 8;

    /// <summary>
    /// Режим SameSite для cookie аутентификации и antiforgery: <c>Lax</c>, <c>Strict</c> или <c>None</c>.
    /// </summary>
    /// <remarks>
    /// <c>None</c> нужен только если SPA живёт на другом сайте; вместе с ним принудительно
    /// включается <c>Secure</c>, иначе браузер cookie не примет.
    /// </remarks>
    public string CookieSameSite { get; init; } = "Lax";

    /// <summary>
    /// Читает секцию и проверяет обязательные значения.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <exception cref="InvalidOperationException">
    /// Ключ подписи отсутствует, короче 32 символов или список предыдущих ключей некорректен.
    /// </exception>
    public static AuthOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        var signingKey = options.SigningKey?.Trim() ?? string.Empty;

        if (signingKey.Length == 0)
        {
            throw new InvalidOperationException(
                """
                Auth:SigningKey is not configured.
                Set it from a secret store or the environment variable Auth__SigningKey.
                The key must be at least 32 characters long and must never be committed.
                """);
        }

        if (signingKey.Length < MIN_SIGNING_KEY_LENGTH)
        {
            throw new InvalidOperationException(
                $"Auth:SigningKey must be at least {MIN_SIGNING_KEY_LENGTH} characters long.");
        }

        var previousKeys = NormalizePreviousKeys(options.PreviousSigningKeys, signingKey);

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Auth:Issuer and Auth:Audience must not be empty.");
        }

        if (options.AccessTokenMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Auth:AccessTokenMinutes must be between 1 and 1440.");
        }

        if (options.RefreshTokenDays is < RefreshToken.MIN_LIFETIME_DAYS
            or > RefreshToken.MAX_LIFETIME_DAYS)
        {
            throw new InvalidOperationException(
                $"Auth:RefreshTokenDays must be between {RefreshToken.MIN_LIFETIME_DAYS} " +
                $"and {RefreshToken.MAX_LIFETIME_DAYS} days.");
        }

        if (options.CookieHours is < 1 or > 168)
        {
            throw new InvalidOperationException("Auth:CookieHours must be between 1 and 168.");
        }

        if (!Enum.TryParse<SameSiteMode>(options.CookieSameSite, ignoreCase: true, out _))
        {
            throw new InvalidOperationException(
                "Auth:CookieSameSite must be one of: Lax, Strict, None.");
        }

        return options with
        {
            SigningKey = signingKey,
            PreviousSigningKeys = previousKeys
        };
    }

    /// <summary>
    /// Разбирает <see cref="CookieSameSite"/>; значение уже проверено в <see cref="FromConfiguration"/>.
    /// </summary>
    public SameSiteMode GetSameSiteMode() => Enum.Parse<SameSiteMode>(CookieSameSite, ignoreCase: true);

    private static string[] NormalizePreviousKeys(string[]? previousKeys, string currentKey)
    {
        if (previousKeys is null || previousKeys.Length == 0)
        {
            return [];
        }

        var normalized = new string[previousKeys.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal) { currentKey };

        for (var index = 0; index < previousKeys.Length; index++)
        {
            var key = previousKeys[index]?.Trim() ?? string.Empty;

            if (key.Length == 0)
            {
                throw new InvalidOperationException(
                    "Auth:PreviousSigningKeys contains an empty key.");
            }

            if (key.Length < MIN_SIGNING_KEY_LENGTH)
            {
                throw new InvalidOperationException(
                    $"Every Auth:PreviousSigningKeys entry must be at least {MIN_SIGNING_KEY_LENGTH} characters long.");
            }

            if (!seen.Add(key))
            {
                throw new InvalidOperationException(
                    "Auth:PreviousSigningKeys must not contain duplicates or the current Auth:SigningKey.");
            }

            normalized[index] = key;
        }

        return normalized;
    }
}
