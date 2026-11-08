using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailVerifications.CleanupEmailVerificationTokens;

/// <summary>
/// Уборка старых одноразовых токенов подтверждения email.
/// </summary>
/// <remarks>
/// Параметров нет: граница считается от <c>TimeProvider</c> и
/// <see cref="EmailVerificationLifetime.TOKEN_RETENTION"/>. Воркер вызывает команду сразу
/// при старте и далее раз в час.
/// </remarks>
public sealed record CleanupEmailVerificationTokensCommand : ICommand;
