using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Единые правила для cookie аутентификации, antiforgery и refresh-токена.
/// </summary>
/// <remarks>
/// <c>SameSite=None</c> нужен только cross-site SPA; вместе с ним браузер требует
/// <c>Secure</c>, иначе cookie не сохраняется. Вне Development <c>Secure</c> включён всегда:
/// префикс <c>__Host-</c> без него недействителен, а за прокси с TLS-терминацией
/// <c>SameAsRequest</c> увидел бы http и не поставил флаг.
/// </remarks>
internal static class AuthCookiePolicy
{
    public static SameSiteMode GetSameSite(AuthOptions options)
        => Enum.Parse<SameSiteMode>(options.CookieSameSite, ignoreCase: true);

    public static CookieSecurePolicy GetSecurePolicy(
        AuthOptions options,
        IWebHostEnvironment environment)
        => !environment.IsDevelopment() || GetSameSite(options) == SameSiteMode.None
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

    public static bool UseSecure(
        AuthOptions options,
        IWebHostEnvironment environment,
        HttpRequest request)
        => GetSecurePolicy(options, environment) switch
        {
            CookieSecurePolicy.Always => true,
            CookieSecurePolicy.SameAsRequest => request.IsHttps,
            _ => false
        };
}
