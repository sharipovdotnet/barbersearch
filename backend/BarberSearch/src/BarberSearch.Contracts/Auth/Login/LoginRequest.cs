namespace BarberSearch.Contracts.Auth.Login;

/// <summary>
/// Учётные данные для входа.
/// </summary>
/// <remarks>
/// <see cref="RememberMe"/> влияет только на cookie-вход: постоянная cookie живёт
/// <c>Auth:CookieHours</c>, сессионная исчезает вместе с браузером. На срок access-токена
/// флаг не влияет.
/// </remarks>
public record LoginRequest(string Email, string Password, bool RememberMe = false);
