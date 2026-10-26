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
/// <para>
/// «Сейчас» берётся из <see cref="TimeProvider"/>, а не из системных часов: и <c>exp</c>,
/// и <c>iat</c> считаются от одних и тех же управляемых часов (R-27, <c>AGENT.md</c> §7.6).
/// Без явного <see cref="SecurityTokenDescriptor.IssuedAt"/> библиотека подставила бы
/// системное время в <c>iat</c>, и тест с фиксированными часами этого бы не увидел.
/// </para>
/// </remarks>
public sealed class JwtTokenIssuer
{
    // Один обработчик на процесс: JsonWebTokenHandler потокобезопасен, а создание экземпляра
    // на каждый выпуск — лишняя инициализация на горячем auth-пути (R-31).
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly AuthOptions _options;
    private readonly JwtSigningKeys _signingKeys;
    private readonly TimeProvider _timeProvider;

    public JwtTokenIssuer(
        AuthOptions options,
        JwtSigningKeys signingKeys,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signingKeys);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _signingKeys = signingKeys;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Выпускает токен для вошедшего пользователя.
    /// </summary>
    public TokenResponse Issue(AuthenticatedUser user)
    {
        // Одно чтение часов на токен: iat и exp не могут разойтись между двумя вызовами.
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(ClaimsPrincipalFactory.CreateClaims(user)),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = now.AddMinutes(_options.AccessTokenMinutes),
            IssuedAt = now,
            SigningCredentials = _signingKeys.Credentials
        };

        var token = Handler.CreateToken(descriptor);

        return new TokenResponse(
            token,
            AuthConstants.BEARER_SCHEME,
            (int)TimeSpan.FromMinutes(_options.AccessTokenMinutes).TotalSeconds);
    }
}
