using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.LoginUser;

namespace BarberSearch.Application.RefreshTokens.RefreshSession;

/// <summary>
/// Обмен refresh-токена на новую пару. Открытый токен приходит из cookie браузера
/// или из тела запроса внешнего клиента.
/// </summary>
public record RefreshSessionCommand(string RefreshToken, int LifetimeDays) : ICommand;

/// <summary>
/// Результат ротации: актуальные данные пользователя для claims, новый открытый refresh-токен
/// и его срок истечения.
/// </summary>
public sealed record RefreshSessionResult(
    AuthenticatedUser User,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
