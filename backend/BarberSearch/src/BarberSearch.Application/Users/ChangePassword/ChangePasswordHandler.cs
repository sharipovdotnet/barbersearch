using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.ChangePassword;

/// <summary>
/// Меняет пароль после проверки текущего и отзывает все refresh-токены пользователя.
/// </summary>
/// <remarks>
/// Отзыв всех токенов, а не только текущего: если пароль сменили из-за подозрения на кражу,
/// остальные устройства не должны пережить смену. Access-токен доживает свои 15 минут —
/// это зафиксированное ограничение JWT (AGENT.md §17.7).
/// </remarks>
public class ChangePasswordHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly IUserRepository _users;
    private readonly IPasswordService _passwords;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChangePasswordHandler> _logger;

    public ChangePasswordHandler(
        IUserRepository users,
        IPasswordService passwords,
        IRefreshTokenRepository refreshTokens,
        TimeProvider timeProvider,
        ILogger<ChangePasswordHandler> logger)
    {
        _users = users;
        _passwords = passwords;
        _refreshTokens = refreshTokens;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет текущий пароль, записывает новый хеш и гасит все refresh-токены.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.NewPassword))
        {
            return new Failure(Error.Validation(
                "auth.changePassword.newPassword",
                "The new password cannot be empty.",
                nameof(command.NewPassword)));
        }

        var userResult = await _users.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        if (_passwords.Verify(user.PasswordHash, command.CurrentPassword) == PasswordVerification.Failed)
        {
            _logger.LogInformation(
                "Password change rejected for user {UserId}: the current password is incorrect.",
                user.Id);

            return new Failure(Error.Validation(
                "auth.changePassword.invalidCurrentPassword",
                "The current password is incorrect.",
                nameof(command.CurrentPassword)));
        }

        var changed = user.ChangePasswordHash(_passwords.Hash(command.NewPassword));
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var saved = await _users.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var revoked = await _refreshTokens.RevokeAllForUserAsync(
            user.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        _logger.LogInformation(
            "User {UserId} changed the password; all refresh tokens have been revoked.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
