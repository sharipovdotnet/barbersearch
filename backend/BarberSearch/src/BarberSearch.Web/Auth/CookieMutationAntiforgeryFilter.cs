using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Глобальная проверка antiforgery для мутаций, которые опираются на cookie.
/// </summary>
/// <remarks>
/// Атрибут на каждом из десятков действий легко забыть при следующей мутации, поэтому
/// проверка одна на весь MVC. Пропуск действует только для запросов, у которых нет
/// ambient-учётных данных: безопасные методы, Bearer-клиент и аноним. Аноним обязан получить
/// <c>401</c> от политики, а не <c>400</c> от фильтра, поэтому проверка идёт после
/// аутентификации и не трогает незаполненный <see cref="System.Security.Principal.ClaimsPrincipal"/>.
/// </remarks>
public sealed class CookieMutationAntiforgeryFilter : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SAFE_METHODS =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.HttpContext.Request;

        if (SAFE_METHODS.Contains(request.Method))
        {
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipCookieAntiforgeryAttribute>().Any())
        {
            return;
        }

        // Bearer-запрос не несёт ambient-учётных данных: CSRF к нему неприменим.
        // Префикс — тот же литерал, по которому SmartScheme выбрала Bearer-схему.
        if (request.Headers.Authorization
            .ToString()
            .StartsWith(AuthConstants.BEARER_PREFIX, StringComparison.Ordinal))
        {
            return;
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

        var failure = await AntiforgeryValidation.ValidateAsync(context.HttpContext, antiforgery);
        if (failure is not null)
        {
            context.Result = FailureProblemDetails.CreateResult(failure);
        }
    }
}
