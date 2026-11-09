using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.PasswordResets.CleanupPasswordResetTokens;

/// <summary>
/// Уборка старых одноразовых токенов сброса пароля.
/// </summary>
/// <remarks>
/// Параметров нет: граница считается от <c>TimeProvider</c> и
/// <see cref="PasswordResetLifetime.TOKEN_RETENTION"/>. Hosted service вызывает команду
/// сразу при старте и далее раз в час.
/// </remarks>
public sealed record CleanupPasswordResetTokensCommand : ICommand;
