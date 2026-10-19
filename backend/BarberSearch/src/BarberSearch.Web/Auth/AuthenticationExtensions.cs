using System.Security.Cryptography;
using System.Text;
using BarberSearch.Application.Users;
using BarberSearch.Domain.RefreshTokens;
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
    /// <param name="configuration">Конфигурация: секция <c>Auth</c>.</param>
    /// <param name="environment">Окружение: в Development допускается ключ, сгенерированный при старте.</param>
    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"""
                     Auth:SigningKey is not configured.
                     Set it with user secrets or the environment variable Auth__SigningKey.
                     The key must be at least 32 characters long.
                     """);
            }

            // Ключ на время жизни процесса: после перезапуска выданные токены станут невалидны.
            // Для разработки этого достаточно, а секрет не попадает в репозиторий.
            options = options with
            {
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
        }

        if (options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters long.");
        }

        if (options.RefreshTokenDays is < RefreshToken.MIN_LIFETIME_DAYS
            or > RefreshToken.MAX_LIFETIME_DAYS)
        {
            throw new InvalidOperationException(
                $"Auth:RefreshTokenDays must be between {RefreshToken.MIN_LIFETIME_DAYS} " +
                $"and {RefreshToken.MAX_LIFETIME_DAYS} days.");
        }

        var sameSite = AuthCookiePolicy.GetSameSite(options);
        var securePolicy = AuthCookiePolicy.GetSecurePolicy(options, environment);

        services.AddSingleton(options);

        // Часы приложения: обработчики refresh-токенов и смены пароля не читают DateTimeOffset.UtcNow
        // напрямую, поэтому срок жизни проверяется в тестах без ожидания.
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<JwtTokenIssuer>();
        services.AddSingleton<IPasswordService, PasswordService>();

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "X-CSRF-TOKEN";
            antiforgery.Cookie.SameSite = sameSite;
            antiforgery.Cookie.SecurePolicy = securePolicy;
        });

        services
            .AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = SmartScheme;
                authentication.DefaultChallengeScheme = SmartScheme;
            })
            .AddPolicyScheme(SmartScheme, "Bearer or Cookie", policy =>
            {
                policy.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ", StringComparison.Ordinal)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = environment.IsDevelopment()
                    ? "barbersearch.auth"
                    : "__Host-barbersearch";

                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = securePolicy;
                cookie.Cookie.SameSite = sameSite;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(options.CookieHours);
                cookie.SlidingExpiration = true;

                cookie.Events.OnRedirectToLogin = context
                    => AuthProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status401Unauthorized,
                        "Authentication is required to access this resource.");

                cookie.Events.OnRedirectToAccessDenied = context
                    => AuthProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "The authenticated user is not allowed to access this resource.");
            })
            .AddJwtBearer(bearer =>
            {
                // Имена claim'ов остаются короткими: sub, role, email.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimsPrincipalFactory.SubClaim,
                    RoleClaimType = ClaimsPrincipalFactory.RoleClaim
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        context.Response.Headers["WWW-Authenticate"] = "Bearer";

                        await AuthProblemDetails.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "A valid access token is required to access this resource.");
                    },
                    OnForbidden = context
                        => AuthProblemDetails.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "The authenticated user is not allowed to access this resource.")
                };
            });

        return services;
    }
}
