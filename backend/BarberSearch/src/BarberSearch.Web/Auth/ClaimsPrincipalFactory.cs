using System.Security.Claims;
using BarberSearch.Application.Users.LoginUser;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Строит claims для cookie и JWT из одного источника.
/// </summary>
/// <remarks>
/// На приёме <c>MapInboundClaims</c> выключен, поэтому имена claim'ов короткие (<c>sub</c>,
/// <c>role</c>), а <c>NameClaimType</c>/<c>RoleClaimType</c> в параметрах валидации указывают сюда же.
/// Один набор claim'ов на обе схемы — иначе политики вели бы себя по-разному в зависимости
/// от того, как клиент вошёл.
/// </remarks>
public static class ClaimsPrincipalFactory
{
    /// <summary>
    /// Идентификатор пользователя.
    /// </summary>
    public const string SubClaim = JwtRegisteredClaimNames.Sub;

    /// <summary>
    /// Адрес электронной почты.
    /// </summary>
    public const string EmailClaim = JwtRegisteredClaimNames.Email;

    /// <summary>
    /// Роль (<c>Customer</c>, <c>Barber</c>, <c>Admin</c>).
    /// </summary>
    public const string RoleClaim = "role";

    /// <summary>
    /// Признак подтверждённого адреса электронной почты.
    /// </summary>
    public const string EmailVerifiedClaim = "email_verified";

    /// <summary>
    /// Идентификатор профиля барбера, если он есть у пользователя.
    /// </summary>
    public const string BarberProfileIdClaim = "barber_profile_id";

    /// <summary>
    /// Уникальный идентификатор токена.
    /// </summary>
    public const string JtiClaim = JwtRegisteredClaimNames.Jti;

    /// <summary>
    /// Собирает claims пользователя.
    /// </summary>
    public static List<Claim> CreateClaims(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(SubClaim, user.Id.ToString()),
            new(EmailClaim, user.Email),
            new(RoleClaim, user.Role.ToString()),
            new(EmailVerifiedClaim, user.IsEmailVerified ? "true" : "false"),
            new(JtiClaim, Guid.NewGuid().ToString())
        };

        if (user.BarberProfileId is { } barberProfileId)
        {
            claims.Add(new Claim(BarberProfileIdClaim, barberProfileId.ToString()));
        }

        return claims;
    }

    /// <summary>
    /// Собирает <see cref="ClaimsPrincipal"/> для cookie-схемы.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user, string authenticationType)
    {
        var identity = new ClaimsIdentity(
            CreateClaims(user),
            authenticationType,
            SubClaim,
            RoleClaim);

        return new ClaimsPrincipal(identity);
    }
}
