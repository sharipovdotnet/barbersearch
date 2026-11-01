namespace BarberSearch.Contracts.Auth.Refresh;

/// <summary>
/// Запрос на ротацию refresh-токена.
/// </summary>
/// <remarks>
/// Браузер оставляет <see cref="RefreshToken"/> пустым: токен приходит в HttpOnly-cookie.
/// Если поле заполнено, ответ вернёт новый refresh в теле, а cookie выставляться не будет.
/// <para>
/// <see cref="RememberMe"/> действует только в cookie-ветке: <c>true</c> делает access-cookie
/// постоянной, <c>false</c> (и отсутствие поля) — сессионной. Наличие поля не переключает
/// ветку на внешнюю: ветка определяется только по <see cref="RefreshToken"/>.
/// </para>
/// </remarks>
public record RefreshTokenRequest(string? RefreshToken = null, bool? RememberMe = null);
