using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Единый ответ на 401/403, которые формирует middleware аутентификации, а не контроллер.
/// </summary>
/// <remarks>
/// Без этого клиент получал бы пустое тело или 302 на страницу входа, тогда как остальные
/// ошибки API приходят как <c>ProblemDetails</c> с кодами в расширении <c>errorCodes</c>.
/// </remarks>
public static class AuthProblemDetails
{
    /// <summary>
    /// Записывает <see cref="ProblemDetails"/> с нужным кодом состояния.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;

        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();

        await service.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == StatusCodes.Status401Unauthorized
                    ? "Authentication is required or the credentials are invalid."
                    : "The authenticated user is not allowed to perform this operation.",
                Detail = detail
            }
        });
    }
}
