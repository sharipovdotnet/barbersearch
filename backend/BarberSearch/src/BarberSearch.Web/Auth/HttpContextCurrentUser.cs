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
/// <para>
/// Анонимный запрос (нет <see cref="HttpContext"/>, нет claim'а <c>sub</c> или он не является
/// идентификатором) не бросает исключение: <see cref="TryGetUserId"/> возвращает <c>false</c>,
/// а решение <c>401</c> принимает слой Application (R-29).
/// </para>
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
    public bool TryGetUserId(out Guid userId)
    {
        var value = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimsPrincipalFactory.SUB_CLAIM);

        return Guid.TryParse(value, out userId);
    }

    /// <inheritdoc />
    public bool IsAdmin
        => string.Equals(
            _httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimsPrincipalFactory.ROLE_CLAIM),
            nameof(UserRole.Admin),
            StringComparison.Ordinal);
}
