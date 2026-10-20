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
            Path = "/",
            SameSite = AuthCookiePolicy.GetSameSite(options),
            Secure = AuthCookiePolicy.UseSecure(options, environment, context.Request)
        };
}
