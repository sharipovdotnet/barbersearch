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
/// <para>
/// Остальные значения заданы один раз — в <c>appsettings.json</c>; тип не дублирует их
/// инициализаторами. Отсутствующее или пустое значение отклоняется в
/// <see cref="FromConfiguration"/>.
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
    /// Максимальный допуск расхождения часов при проверке JWT в секундах.
    /// </summary>
    public const int MAX_CLOCK_SKEW_SECONDS = 300;

    /// <summary>
    /// Издатель токена (<c>iss</c>). Значение задаётся в <c>appsettings.json</c>.
    /// </summary>
    public string Issuer { get; init; } = null!;

    /// <summary>
    /// Получатель токена (<c>aud</c>) — идентификатор этого API. Значение задаётся
    /// в <c>appsettings.json</c>.
    /// </summary>
    public string Audience { get; init; } = null!;

    /// <summary>
    /// Текущий ключ подписи HMAC-SHA256. Не хранится в репозитории.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Предыдущие ключи подписи, которые ещё принимаются при проверке токенов.
    /// </summary>
    public string[] PreviousSigningKeys { get; init; } = [];

    /// <summary>
    /// Срок жизни access-токена в минутах. Значение задаётся в <c>appsettings.json</c>.
    /// </summary>
    public int AccessTokenMinutes { get; init; }

    /// <summary>
    /// Срок жизни refresh-токена в днях. Допустимо 7–30: короткий срок заставляет клиента
    /// часто обновляться, длинный — растягивает окно украденного токена. Значение задаётся
    /// в <c>appsettings.json</c>.
    /// </summary>
    public int RefreshTokenDays { get; init; }

    /// <summary>
    /// Срок жизни постоянной cookie в часах. Значение задаётся в <c>appsettings.json</c>.
    /// </summary>
    public int CookieHours { get; init; }

    /// <summary>
    /// Режим SameSite для cookie аутентификации и antiforgery: <c>Lax</c>, <c>Strict</c> или <c>None</c>.
    /// Значение задаётся в <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// <c>None</c> нужен только если SPA живёт на другом сайте; вместе с ним принудительно
    /// включается <c>Secure</c>, иначе браузер cookie не примет.
    /// </remarks>
    public string CookieSameSite { get; init; } = null!;

    /// <summary>
    /// Допуск на расхождение часов при проверке JWT в секундах, <c>0..300</c>.
    /// Значение задаётся в <c>appsettings.json</c>; настраиваемый допуск заменяет прежние
    /// жёстко заданные 30 секунд.
    /// </summary>
    public int ClockSkewSeconds { get; init; }

    /// <summary>
    /// Читает секцию и проверяет обязательные значения.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <exception cref="InvalidOperationException">
    /// Секция <c>Auth</c> отсутствует, обязательное значение пустое или вне диапазона,
    /// ключ подписи отсутствует, короче 32 символов, либо список предыдущих ключей некорректен.
    /// </exception>
    public static AuthOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        // Значения по умолчанию живут в appsettings.json, а не в этом типе. Если секции нет,
        // молчаливая подстановка new AuthOptions() вернула бы незаполненные свойства и
        // замаскировала ошибку конфигурации.
        if (!section.Exists())
        {
            throw new InvalidOperationException(
                """
                Auth configuration section is not configured.
                Add the Auth section to appsettings.json with Issuer, Audience,
                AccessTokenMinutes, RefreshTokenDays, CookieHours, CookieSameSite and
                ClockSkewSeconds.
                Auth:SigningKey must come from a secret store or the environment variable Auth__SigningKey.
                """);
        }

        var options = section.Get<AuthOptions>()
            ?? throw new InvalidOperationException("Auth configuration section could not be read.");

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

        if (options.ClockSkewSeconds is < 0 or > MAX_CLOCK_SKEW_SECONDS)
        {
            throw new InvalidOperationException(
                $"Auth:ClockSkewSeconds must be between 0 and {MAX_CLOCK_SKEW_SECONDS} seconds.");
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
