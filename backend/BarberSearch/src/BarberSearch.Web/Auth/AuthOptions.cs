namespace BarberSearch.Web.Auth;

/// <summary>
/// Настройки аутентификации из секции <c>Auth</c>.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> в репозитории не хранится: в Development ключ генерируется при старте,
/// в остальных окружениях обязан прийти из user secrets или переменной окружения
/// <c>Auth__SigningKey</c> и быть не короче 32 символов.
/// </remarks>
public sealed record AuthOptions
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Издатель токена (<c>iss</c>).
    /// </summary>
    public string Issuer { get; init; } = "BarberSearch";

    /// <summary>
    /// Получатель токена (<c>aud</c>) — идентификатор этого API.
    /// </summary>
    public string Audience { get; init; } = "BarberSearch.Api";

    /// <summary>
    /// Ключ подписи HMAC-SHA256. Не хранится в репозитории.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Срок жизни access-токена в минутах.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;

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
}
