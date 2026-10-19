namespace BarberSearch.Contracts.Auth.Refresh;

/// <summary>
/// Запрос на ротацию refresh-токена внешним клиентом.
/// </summary>
/// <remarks>
/// Браузер оставляет тело пустым: токен приходит в HttpOnly-cookie. Если поле заполнено,
/// ответ вернёт новый refresh в теле, а cookie выставляться не будет.
/// </remarks>
public record RefreshTokenRequest(string? RefreshToken = null);
