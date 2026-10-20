using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.RefreshTokens.RevokeRefreshToken;

/// <summary>
/// Отзывает один refresh-токен при logout.
/// </summary>
/// <remarks>
/// Неизвестный или уже отозванный токен — не ошибка: logout идемпотентен и не должен
/// раскрывать, существовал ли токен. Другие сессии пользователя при этом остаются живыми.
/// </remarks>
public class RevokeRefreshTokenHandler : ICommandHandler<RevokeRefreshTokenCommand>
{
    private readonly IRefreshTokenRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RevokeRefreshTokenHandler> _logger;

    public RevokeRefreshTokenHandler(
        IRefreshTokenRepository repository,
        TimeProvider timeProvider,
        ILogger<RevokeRefreshTokenHandler> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Находит токен по хешу и помечает его отозванным.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RevokeRefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return UnitResult.Success<Failure>();
        }

        var hash = RefreshTokenGenerator.ComputeHash(command.RefreshToken.Trim());

        var tokenResult = await _repository.GetByHashAsync(hash, cancellationToken);
        if (tokenResult.IsFailure)
        {
            // Токен не найден или уже удалён каскадом вместе с пользователем — отзывать нечего.
            return UnitResult.Success<Failure>();
        }

        var token = tokenResult.Value;

        if (token.IsRevoked)
        {
            return UnitResult.Success<Failure>();
        }

        var revoke = token.Revoke(_timeProvider.GetUtcNow());
        if (revoke.IsFailure)
        {
            return revoke.Error;
        }

        var saved = await _repository.RevokeAsync(token, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Refresh token revoked for user {UserId} (logout).",
            token.UserId);

        return UnitResult.Success<Failure>();
    }
}
