using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Auth;

/// <summary>
/// HttpOnly-cookie с refresh-токеном браузерной ветки. JavaScript его не читает, поэтому
/// XSS не получает долгоживущий токен; на сервер он возвращается только в <c>POST api/auth/refresh</c>.
/// </summary>
/// <remarks>
/// В Development имя без префикса — приложение ходит по http. Вне Development — <c>__Host-</c>:
/// такой префикс требует <c>Secure</c>, <c>Path=/</c> и отсутствие <c>Domain</c>, и это
/// гарантирует, что cookie не подменит соседний поддомен.
/// <para>
/// R-49: <c>Path=/</c> — требование префикса <c>__Host-</c>, поэтому сузить путь до
/// <c>/api/auth</c>, сохранив префикс, нельзя. Это осознанно принято: <c>Path</c> не является
/// границей доверия — cookie уходит только на тот же origin, который и так обслуживает
/// <c>/api/auth/*</c>, она <c>HttpOnly</c>, <c>Secure</c> и <c>SameSite</c>, поэтому скрипты её
/// не читают, а сторонних CDN и логов, куда мог бы попасть токен, в проекте нет. Взамен
/// <c>__Host-</c> запрещает <c>Domain</c> и не даёт соседнему поддомену перекрыть refresh-cookie —
/// свойство сильнее, чем экономия нескольких байт в заголовках запросов.
/// </para>
/// <para>
/// Пересмотреть, если публичные пути переедут на отдельный origin/CDN или префикс <c>__Host-</c>
/// будет снят по другой причине: тогда <c>Path=/api/auth</c> (или отдельный cookie-хост) вместе
/// с миграцией имени cookie.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    /// <summary>
    /// Имя cookie в Development.
    /// </summary>
    public const string DEVELOPMENT_COOKIE_NAME = "barbersearch.refresh";

    /// <summary>
    /// Имя cookie вне Development.
    /// </summary>
    public const string PRODUCTION_COOKIE_NAME = "__Host-barbersearch-refresh";

    /// <summary>
    /// Имя cookie для текущего окружения.
    /// </summary>
    public static string GetName(IWebHostEnvironment environment)
        => environment.IsDevelopment()
            ? DEVELOPMENT_COOKIE_NAME
            : PRODUCTION_COOKIE_NAME;

    /// <summary>
    /// Ставит refresh-токен в HttpOnly-cookie с абсолютным сроком жизни токена.
    /// </summary>
    public static void Append(
        HttpContext context,
        IWebHostEnvironment environment,
        AuthOptions options,
        string token,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var cookie = CreateOptions(context, environment, options);
        cookie.Expires = expiresAt;

        context.Response.Cookies.Append(GetName(environment), token, cookie);
    }

    /// <summary>
    /// Удаляет refresh-cookie (logout, неудачный refresh, смена пароля).
    /// </summary>
    public static void Delete(
        HttpContext context,
        IWebHostEnvironment environment,
        AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(GetName(environment), CreateOptions(context, environment, options));
    }

    private static CookieOptions CreateOptions(
        HttpContext context,
        IWebHostEnvironment environment,
        AuthOptions options)
        => new()
        {
            HttpOnly = true,
            IsEssential = true,
            Path = AuthConstants.COOKIE_PATH,
            SameSite = AuthCookiePolicy.GetSameSite(options),
            Secure = AuthCookiePolicy.UseSecure(options, environment, context.Request)
        };
}
