namespace BarberSearch.Contracts.Auth.PasswordReset;

/// <summary>
/// Тело запроса смены пароля по одноразовой ссылке из письма.
/// </summary>
/// <remarks>
/// Токен nullable только в контракте: его отсутствие — ошибка формата
/// (<c>400 auth.passwordReset.invalid</c>), а не ошибка разбора JSON. Секрет — сам токен,
/// поэтому ни пользователь, ни cookie для подтверждения не нужны.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки <c>/reset-password?token=…</c>.</param>
/// <param name="NewPassword">Новый пароль; проверяется политикой как поле <c>newPassword</c>.</param>
public sealed record ConfirmPasswordResetRequest(string? Token, string? NewPassword);
