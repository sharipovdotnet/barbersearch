using BarberSearch.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Политики авторизации и правило по умолчанию.
/// </summary>
/// <remarks>
/// <c>FallbackPolicy</c> закрывает всё, что не помечено явно: новый контроллер или эндпоинт
/// сразу требует аутентификации, а не забывается открытым. Публичные действия помечаются
/// <c>[AllowAnonymous]</c> осознанно.
/// </remarks>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Регистрирует политики и fallback-правило.
    /// </summary>
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(AuthorizationPolicies.AdminOnly, policy
                => policy.RequireRole(nameof(UserRole.Admin)));

            options.AddPolicy(AuthorizationPolicies.BarberOnly, policy
                => policy.RequireRole(nameof(UserRole.Barber)));

            options.AddPolicy(AuthorizationPolicies.VerifiedEmail, policy
                => policy.RequireClaim(ClaimsPrincipalFactory.EMAIL_VERIFIED_CLAIM, ClaimValues.TRUE));
        });

        return services;
    }
}
