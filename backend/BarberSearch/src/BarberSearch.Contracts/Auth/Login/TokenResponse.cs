namespace BarberSearch.Contracts.Auth.Login;

/// <summary>
/// Параметры выданной пары access/refresh.
/// </summary>
/// <remarks>
/// <see cref="RefreshToken"/> заполняется только для внешнего клиента: браузер получает
/// refresh в HttpOnly-cookie, и тело ответа его не содержит. Срок refresh — в секундах,
/// как и срок access; браузерной ветке он не нужен.
/// </remarks>
public record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string? RefreshToken = null,
    int? RefreshTokenExpiresInSeconds = null);
