using Microsoft.AspNetCore.Antiforgery;
using Shared;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Проверка antiforgery для действий, которые полагаются на cookie.
/// </summary>
/// <remarks>
/// Вынесена из <see cref="ValidateAntiforgeryHeaderAttribute"/>, потому что <c>refresh</c>
/// и <c>logout</c> проверяют токен только в cookie-ветке: запрос с refresh-токеном в теле
/// не опирается на cookie и CSRF-атаке не подвержен.
/// </remarks>
internal static class AntiforgeryValidation
{
    /// <summary>
    /// Проверяет токен; <c>null</c> — запрос прошёл проверку.
    /// </summary>
    public static async Task<Failure?> ValidateAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);

            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return new Failure(Error.Validation(
                ValidateAntiforgeryHeaderAttribute.ErrorCode,
                "The antiforgery token is missing or invalid. Request GET api/auth/csrf first."));
        }
    }
}
