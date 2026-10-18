using System.Security.Claims;
using System.Text;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Contracts.Auth.Login;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Выдаёт подписанный access-токен.
/// </summary>
/// <remarks>
/// Подпись — HMAC-SHA256: этот же API и проверяет свои токены, поэтому асимметричный ключ
/// не нужен. Срок жизни короткий (по умолчанию 15 минут), потому что отозвать JWT до истечения
/// нельзя; за это время успевает примениться смена роли или деактивация.
/// </remarks>
public sealed class JwtTokenIssuer
{
    private readonly AuthOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenIssuer(AuthOptions options)
    {
        _options = options;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// Выпускает токен для вошедшего пользователя.
    /// </summary>
    public TokenResponse Issue(AuthenticatedUser user)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(ClaimsPrincipalFactory.CreateClaims(user)),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            SigningCredentials = _credentials
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new TokenResponse(
            token,
            "Bearer",
            (int)TimeSpan.FromMinutes(_options.AccessTokenMinutes).TotalSeconds);
    }
}
