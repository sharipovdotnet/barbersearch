using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PasswordResets.ConfirmPasswordReset;

/// <summary>
/// Установка нового пароля по одноразовой ссылке из письма.
/// </summary>
/// <remarks>
/// Команда анонимная: секрет — сам токен, cookie и claims не используются. Повторная
/// отправка того же токена не идемпотентна: успешный сброс потребляет токен, и второй
/// запрос получает <c>400 auth.passwordReset.invalid</c>.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки; формат проверяется до хеширования.</param>
/// <param name="NewPassword">Новый пароль; проверяется политикой до хеширования.</param>
public sealed record ConfirmPasswordResetCommand(string Token, string NewPassword) : ICommand;
