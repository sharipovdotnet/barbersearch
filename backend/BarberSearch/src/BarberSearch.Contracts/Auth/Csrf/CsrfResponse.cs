namespace BarberSearch.Contracts.Auth.Csrf;

/// <summary>
/// Токен защиты от CSRF для cookie-входа и выхода.
/// </summary>
/// <remarks>
/// Клиент получает его анонимным <c>GET api/auth/csrf</c> и отправляет обратно
/// в заголовке <c>X-CSRF-TOKEN</c>. Cookie с этим токеном ставится тем же ответом.
/// </remarks>
public record CsrfResponse(string Token);
