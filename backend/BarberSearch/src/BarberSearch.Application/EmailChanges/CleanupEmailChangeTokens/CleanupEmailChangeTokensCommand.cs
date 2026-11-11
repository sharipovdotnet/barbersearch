using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailChanges.CleanupEmailChangeTokens;

/// <summary>
/// Уборка старых одноразовых токенов смены адреса электронной почты.
/// </summary>
/// <remarks>
/// Параметров нет: граница считается от <c>TimeProvider</c> и
/// <see cref="EmailChangeLifetime.TOKEN_RETENTION"/>. Воркер вызывает команду сразу при старте
/// и далее раз в час.
/// </remarks>
public sealed record CleanupEmailChangeTokensCommand : ICommand;
