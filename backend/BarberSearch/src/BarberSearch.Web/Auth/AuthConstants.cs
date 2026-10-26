namespace BarberSearch.Web.Auth;

/// <summary>
/// Литералы аутентификации, которые не должны расползаться по Web-слою.
/// </summary>
/// <remarks>
/// Имена cookie, CSRF-заголовка и схемы — часть контракта с браузером и клиентами:
/// смена значения здесь меняет поведение, поэтому все места обязаны брать его отсюда.
/// Тексты 401/403 вынесены отдельно от кодов состояния: заголовок <c>ProblemDetails</c>
/// и <c>detail</c> отличаются по смыслу, но не должны дублироваться в двух классах.
/// </remarks>
public static class AuthConstants
{
    /// <summary>
    /// Имя cookie сессии в Development.
    /// </summary>
    public const string DEVELOPMENT_SESSION_COOKIE = "barbersearch.auth";

    /// <summary>
    /// Имя cookie сессии вне Development: префикс <c>__Host-</c> требует <c>Secure</c>,
    /// <c>Path=/</c> и отсутствие <c>Domain</c>.
    /// </summary>
    public const string PRODUCTION_SESSION_COOKIE = "__Host-barbersearch";

    /// <summary>
    /// Заголовок с antiforgery-токеном.
    /// </summary>
    public const string CSRF_HEADER_NAME = "X-CSRF-TOKEN";

    /// <summary>
    /// Путь cookie аутентификации, antiforgery и refresh-токена.
    /// </summary>
    public const string COOKIE_PATH = "/";

    /// <summary>
    /// Префикс значения заголовка <c>Authorization</c> для bearer-токена.
    /// </summary>
    public const string BEARER_PREFIX = "Bearer ";

    /// <summary>
    /// Имя схемы bearer в ответах и OpenAPI.
    /// </summary>
    public const string BEARER_SCHEME = "Bearer";

    /// <summary>
    /// Заголовок, которым JWT-middleware сообщает способ аутентификации.
    /// </summary>
    public const string WWW_AUTHENTICATE_HEADER = "WWW-Authenticate";

    /// <summary>
    /// Display name схемы-селектора в диагностике ASP.NET Core.
    /// </summary>
    public const string SMART_SCHEME_DISPLAY_NAME = "Bearer or Cookie";

    /// <summary>
    /// <c>detail</c> для 401 в cookie-ветке: пользователь не представился.
    /// </summary>
    public const string AUTH_REQUIRED_DETAIL =
        "Authentication is required to access this resource.";

    /// <summary>
    /// <c>detail</c> для 401 в JWT-ветке: токен отсутствует или недействителен.
    /// </summary>
    public const string INVALID_TOKEN_DETAIL =
        "A valid access token is required to access this resource.";

    /// <summary>
    /// <c>detail</c> для 403 в обеих ветках.
    /// </summary>
    public const string ACCESS_DENIED_DETAIL =
        "The authenticated user is not allowed to access this resource.";

    /// <summary>
    /// Причина отказа <c>OnTokenValidated</c>, когда security stamp не совпал.
    /// </summary>
    public const string INVALID_SECURITY_STAMP_MESSAGE = "The security stamp is invalid.";

    /// <summary>
    /// Заголовок <c>ProblemDetails</c> для 401 (общий для middleware и контроллеров).
    /// </summary>
    public const string AUTHENTICATION_FAILED_TITLE =
        "Authentication is required or the credentials are invalid.";

    /// <summary>
    /// Заголовок <c>ProblemDetails</c> для 403 (общий для middleware и контроллеров).
    /// </summary>
    public const string ACCESS_DENIED_TITLE =
        "The authenticated user is not allowed to perform this operation.";
}
