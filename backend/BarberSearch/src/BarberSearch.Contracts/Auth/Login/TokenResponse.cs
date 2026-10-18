namespace BarberSearch.Contracts.Auth.Login;

/// <summary>
/// Параметры выданного access-токена.
/// </summary>
public record TokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);
