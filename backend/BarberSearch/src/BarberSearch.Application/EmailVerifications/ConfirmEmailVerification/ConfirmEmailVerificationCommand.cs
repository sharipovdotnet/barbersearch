using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailVerifications.ConfirmEmailVerification;

/// <summary>
/// Подтверждение адреса электронной почты по одноразовой ссылке.
/// </summary>
/// <remarks>
/// Секрет — сам токен: команда анонимная и не опирается на cookie или claims. Повторный
/// клик по ссылке и уже подтверждённый адрес идемпотентны.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки; формат проверяется до хеширования.</param>
public sealed record ConfirmEmailVerificationCommand(string Token) : ICommand;
