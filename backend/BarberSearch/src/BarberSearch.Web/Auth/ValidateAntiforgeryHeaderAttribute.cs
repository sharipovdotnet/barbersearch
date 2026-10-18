using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shared;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Проверяет antiforgery-токен для действий, которые полагаются на cookie.
/// </summary>
/// <remarks>
/// Штатный <c>[ValidateAntiForgeryToken]</c> живёт в MVC ViewFeatures и требует
/// <c>AddControllersWithViews()</c>; API регистрирует только контроллеры, поэтому фильтр
/// написан здесь и проверяет токен через <see cref="IAntiforgery"/> — тот же сервис,
/// что выдаёт его в <c>GET api/auth/csrf</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateAntiforgeryHeaderAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// Код ошибки для отсутствующего или просроченного токена.
    /// </summary>
    public const string ErrorCode = "auth.csrf.invalid";

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = FailureProblemDetails.CreateResult(new Failure(Error.Validation(
                ErrorCode,
                "The antiforgery token is missing or invalid. Request GET api/auth/csrf first.")));
        }
    }
}
