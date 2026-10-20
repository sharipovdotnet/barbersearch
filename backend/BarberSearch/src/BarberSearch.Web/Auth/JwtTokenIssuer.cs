using System.Security.Claims;
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
/// <para>
/// Текущий ключ подписи задаётся в <see cref="JwtSigningKeys"/>; предыдущие ключи там же
/// используются только для проверки, поэтому ротация не инвалидирует уже выданные токены.
/// </para>
/// </remarks>
public sealed class JwtTokenIssuer
{
    private readonly AuthOptions _options;
    private readonly JwtSigningKeys _signingKeys;

    public JwtTokenIssuer(AuthOptions options, JwtSigningKeys signingKeys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signingKeys);

        _options = options;
        _signingKeys = signingKeys;
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
            SigningCredentials = _signingKeys.Credentials
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new TokenResponse(
            token,
            "Bearer",
            (int)TimeSpan.FromMinutes(_options.AccessTokenMinutes).TotalSeconds);
    }
}
