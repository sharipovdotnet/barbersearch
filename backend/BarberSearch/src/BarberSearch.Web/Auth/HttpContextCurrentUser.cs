using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;

namespace BarberSearch.Web.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> поверх <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// Claims читаются через <see cref="ClaimsPrincipalFactory"/>: имена одинаковы для cookie и JWT,
/// поэтому проверки владения не зависят от схемы, которой вошёл клиент.
/// </remarks>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimsPrincipalFactory.SubClaim);

            return Guid.TryParse(value, out var userId)
                ? userId
                : throw new InvalidOperationException(
                    "The current request does not carry an authenticated user identifier.");
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
        => string.Equals(
            _httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimsPrincipalFactory.RoleClaim),
            nameof(UserRole.Admin),
            StringComparison.Ordinal);
}
