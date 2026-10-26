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
    public const string SUB_CLAIM = JwtRegisteredClaimNames.Sub;

    /// <summary>
    /// Адрес электронной почты.
    /// </summary>
    public const string EMAIL_CLAIM = JwtRegisteredClaimNames.Email;

    /// <summary>
    /// Роль (<c>Customer</c>, <c>Barber</c>, <c>Admin</c>).
    /// </summary>
    public const string ROLE_CLAIM = "role";

    /// <summary>
    /// Признак подтверждённого адреса электронной почты.
    /// </summary>
    public const string EMAIL_VERIFIED_CLAIM = "email_verified";

    /// <summary>
    /// Идентификатор профиля барбера, если он есть у пользователя.
    /// </summary>
    public const string BARBER_PROFILE_ID_CLAIM = "barber_profile_id";

    /// <summary>
    /// Отпечаток состояния учётных данных. Сверяется с <c>User.SecurityStamp</c> на каждом
    /// запросе, поэтому смена пароля, роли или деактивация отзывают выданные токены.
    /// </summary>
    public const string SECURITY_STAMP_CLAIM = "security_stamp";

    /// <summary>
    /// Уникальный идентификатор токена.
    /// </summary>
    public const string JTI_CLAIM = JwtRegisteredClaimNames.Jti;

    /// <summary>
    /// Собирает claims пользователя.
    /// </summary>
    public static List<Claim> CreateClaims(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(SUB_CLAIM, user.Id.ToString()),
            new(EMAIL_CLAIM, user.Email),
            new(ROLE_CLAIM, user.Role.ToString()),
            new(EMAIL_VERIFIED_CLAIM, user.IsEmailVerified ? "true" : "false"),
            new(SECURITY_STAMP_CLAIM, user.SecurityStamp),
            new(JTI_CLAIM, Guid.NewGuid().ToString())
        };

        if (user.BarberProfileId is { } barberProfileId)
        {
            claims.Add(new Claim(BARBER_PROFILE_ID_CLAIM, barberProfileId.ToString()));
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
            SUB_CLAIM,
            ROLE_CLAIM);

        return new ClaimsPrincipal(identity);
    }
}
