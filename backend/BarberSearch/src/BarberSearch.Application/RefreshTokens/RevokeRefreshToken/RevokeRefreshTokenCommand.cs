using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.RefreshTokens.RevokeRefreshToken;

/// <summary>
/// Отзывает предъявленный refresh-токен (logout). Пустое значение — идемпотентный logout
/// без refresh-токена: cookie всё равно гасится контроллером.
/// </summary>
public record RevokeRefreshTokenCommand(string? RefreshToken) : ICommand;
