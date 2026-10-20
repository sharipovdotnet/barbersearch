namespace BarberSearch.Contracts.Auth.Logout;

/// <summary>
/// Запрос на выход внешнего клиента: отзывает refresh-токен из тела.
/// </summary>
/// <remarks>
/// Браузеру тело не нужно — токен берётся из HttpOnly-cookie. Пустое поле делает logout
/// идемпотентным: cookie всё равно гасится.
/// </remarks>
public record LogoutRequest(string? RefreshToken = null);
