namespace BarberSearch.Contracts.Auth.EmailVerification;

/// <summary>
/// Тело запроса подтверждения email по ссылке.
/// </summary>
/// <remarks>
/// Токен nullable только в контракте: отсутствие токена — это ошибка формата
/// (<c>400 auth.emailVerification.invalid</c>), а не ошибка разбора JSON. Секрет — сам токен,
/// поэтому ни пользователь, ни cookie для подтверждения не нужны.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки <c>/verify-email?token=…</c>.</param>
public sealed record ConfirmEmailVerificationRequest(string? Token);
