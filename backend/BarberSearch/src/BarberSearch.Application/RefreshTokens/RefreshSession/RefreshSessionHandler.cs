using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.RefreshTokens.RefreshSession;

/// <summary>
/// Ротирует refresh-токен: старый помечается <c>RevokedAt</c> + <c>ReplacedBy</c>,
/// выдаётся новый. Повторное использование заменённого токена отзывает всю цепочку.
/// </summary>
/// <remarks>
/// Пользователь перечитывается при каждом refresh, поэтому claims нового access-токена
/// отражают актуальную роль и <c>BarberProfileId</c>, а деактивированный пользователь
/// не может продлить сессию даже живым refresh-токеном.
/// </remarks>
public class RefreshSessionHandler : ICommandHandler<RefreshSessionResult, RefreshSessionCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserRepository _users;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshSessionHandler> _logger;

    public RefreshSessionHandler(
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        TimeProvider timeProvider,
        ILogger<RefreshSessionHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _users = users;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет токен, отзывает его и выпускает замену.
    /// </summary>
    public async Task<Result<RefreshSessionResult, Failure>> Handle(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Unauthorized("auth.refresh.missing", "A refresh token is required.");
        }

        if (RefreshTokenLifetime.Validate(command.LifetimeDays) is { } lifetimeError)
        {
            return lifetimeError;
        }

        var now = _timeProvider.GetUtcNow();
        var hash = RefreshTokenGenerator.ComputeHash(command.RefreshToken.Trim());

        var tokenResult = await _refreshTokens.GetByHashAsync(hash, cancellationToken);
        if (tokenResult.IsFailure)
        {
            _logger.LogInformation("Refresh attempt with an unknown token.");

            return Unauthorized("auth.refresh.invalid", "The refresh token is invalid.");
        }

        var current = tokenResult.Value;

        if (current.IsReplaced)
        {
            // Заменённый токен предъявлен повторно: либо украден, либо клиент сохранил старую копию.
            // Безопаснее погасить всю цепочку, чем оставить активным её последний токен.
            var revoked = await _refreshTokens.RevokeChainAsync(current.Id, now, cancellationToken);
            if (revoked.IsFailure)
            {
                _logger.LogError(
                    "Failed to revoke the refresh token chain for user {UserId}: {ErrorCodes}.",
                    current.UserId,
                    string.Join(", ", revoked.Error.Errors.Select(error => error.Code)));
            }

            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; the whole chain has been revoked.",
                current.UserId);

            return Unauthorized(
                "auth.refresh.reused",
                "The refresh token has already been used. The session has been revoked.");
        }

        if (current.IsRevoked)
        {
            return Unauthorized("auth.refresh.revoked", "The refresh token has been revoked.");
        }

        if (current.ExpiresAt <= now)
        {
            return Unauthorized("auth.refresh.expired", "The refresh token has expired.");
        }

        var userResult = await _users.GetByIdAsync(current.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return Unauthorized("auth.refresh.invalid", "The refresh token is invalid.");
        }

        var user = userResult.Value;

        if (!user.IsActive)
        {
            var revoked = await _refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
            if (revoked.IsFailure)
            {
                _logger.LogError(
                    "Failed to revoke refresh tokens of deactivated user {UserId}: {ErrorCodes}.",
                    user.Id,
                    string.Join(", ", revoked.Error.Errors.Select(error => error.Code)));
            }

            return Unauthorized("auth.refresh.inactive", "The account is deactivated.");
        }

        var rawToken = RefreshTokenGenerator.CreateToken();

        var replacementResult = RefreshToken.Create(
            user.Id,
            RefreshTokenGenerator.ComputeHash(rawToken),
            now.AddDays(command.LifetimeDays),
            now);

        if (replacementResult.IsFailure)
        {
            return replacementResult.Error;
        }

        var replacement = replacementResult.Value;

        var revoke = current.Revoke(now, replacement.Id);
        if (revoke.IsFailure)
        {
            return revoke.Error;
        }

        var rotated = await _refreshTokens.RotateAsync(current, replacement, cancellationToken);
        if (rotated.IsFailure)
        {
            return rotated.Error;
        }

        _logger.LogInformation(
            "Refresh token rotated for user {UserId}; the replacement expires at {ExpiresAt}.",
            user.Id,
            replacement.ExpiresAt);

        return new RefreshSessionResult(
            AuthenticatedUser.From(user),
            rawToken,
            replacement.ExpiresAt);
    }

    private static Failure Unauthorized(string code, string message)
        => new(Error.Unauthorized(code, message));
}
