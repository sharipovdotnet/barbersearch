using BarberSearch.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Подключает обе схемы аутентификации и выбирает одну из них по заголовку запроса.
/// </summary>
/// <remarks>
/// Браузер получает cookie, мобильный или внешний клиент — bearer-токен. Селектор избавляет
/// от <c>[Authorize(AuthenticationSchemes = ...)]</c> на каждом действии: без него легко
/// забыть схему на одном эндпоинте и получить 401 там, где токен передан правильно.
/// </remarks>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Имя схемы-селектора.
    /// </summary>
    public const string SmartScheme = "Smart";

    /// <summary>
    /// Регистрирует аутентификацию, antiforgery и сервисы выдачи учётных данных.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="options">Уже проверенные настройки секции <c>Auth</c>.</param>
    /// <param name="environment">Окружение: имена cookie и политика <c>Secure</c>.</param>
    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services,
        AuthOptions options,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var sameSite = AuthCookiePolicy.GetSameSite(options);
        var securePolicy = AuthCookiePolicy.GetSecurePolicy(options, environment);
        var signingKeys = new JwtSigningKeys(options);

        services.AddSingleton(options);
        services.AddSingleton(signingKeys);

        // Часы приложения: обработчики, выдача JWT и cookie не читают DateTimeOffset.UtcNow
        // напрямую, поэтому сроки и метки токенов проверяются в тестах без ожидания.
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<JwtTokenIssuer>();
        services.AddSingleton<IPasswordService, PasswordService>();

        // Scoped: проверка отпечатка читает пользователя через scoped DbContext.
        services.AddScoped<SecurityStampValidator>();

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = AuthConstants.CSRF_HEADER_NAME;
            antiforgery.Cookie.Path = AuthConstants.COOKIE_PATH;
            antiforgery.Cookie.SameSite = sameSite;
            antiforgery.Cookie.SecurePolicy = securePolicy;
        });

        services
            .AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = SmartScheme;
                authentication.DefaultChallengeScheme = SmartScheme;
            })
            .AddPolicyScheme(SmartScheme, AuthConstants.SMART_SCHEME_DISPLAY_NAME, policy =>
            {
                policy.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString()
                        .StartsWith(AuthConstants.BEARER_PREFIX, StringComparison.Ordinal)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = environment.IsDevelopment()
                    ? AuthConstants.DEVELOPMENT_SESSION_COOKIE
                    : AuthConstants.PRODUCTION_SESSION_COOKIE;

                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.Path = AuthConstants.COOKIE_PATH;
                cookie.Cookie.SecurePolicy = securePolicy;
                cookie.Cookie.SameSite = sameSite;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(options.CookieHours);
                cookie.SlidingExpiration = true;

                cookie.Events.OnRedirectToLogin = context
                    => AuthProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status401Unauthorized,
                        AuthConstants.AUTH_REQUIRED_DETAIL);

                cookie.Events.OnRedirectToAccessDenied = context
                    => AuthProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        AuthConstants.ACCESS_DENIED_DETAIL);

                cookie.Events.OnValidatePrincipal = async context =>
                {
                    var validator = context.HttpContext.RequestServices
                        .GetRequiredService<SecurityStampValidator>();

                    if (await validator.ValidateAsync(
                            context.Principal,
                            context.HttpContext.RequestAborted))
                    {
                        return;
                    }

                    // Principal отклоняется, cookie удаляется. Ответ 401 сформирует
                    // OnRedirectToLogin при challenge, поэтому здесь нет ни 302, ни второго тела.
                    context.RejectPrincipal();

                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);
                };
            })
            .AddJwtBearer(bearer =>
            {
                // Имена claim'ов остаются короткими: sub, role, email.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKeys = signingKeys.ValidationKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    TryAllIssuerSigningKeys = true,
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                    NameClaimType = ClaimsPrincipalFactory.SUB_CLAIM,
                    RoleClaimType = ClaimsPrincipalFactory.ROLE_CLAIM
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<SecurityStampValidator>();

                        if (await validator.ValidateAsync(
                                context.Principal,
                                context.HttpContext.RequestAborted))
                        {
                            return;
                        }

                        // Fail, а не запись тела: на защищённом эндпоинте OnChallenge ниже
                        // отдаст 401 ProblemDetails, а [AllowAnonymous] запрос не сломается.
                        context.Fail(AuthConstants.INVALID_SECURITY_STAMP_MESSAGE);
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        context.Response.Headers[AuthConstants.WWW_AUTHENTICATE_HEADER] =
                            AuthConstants.BEARER_SCHEME;

                        await AuthProblemDetails.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            AuthConstants.INVALID_TOKEN_DETAIL);
                    },
                    OnForbidden = context
                        => AuthProblemDetails.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            AuthConstants.ACCESS_DENIED_DETAIL)
                };
            });

        return services;
    }
}
