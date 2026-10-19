using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.ChangeUserRole;

/// <summary>
/// Обработчик команды <see cref="ChangeUserRoleCommand"/>.
/// </summary>
/// <remarks>
/// Допустимость значения роли проверяет домен: неизвестный <see cref="UserRole"/> отклоняется там же.
/// </remarks>
public class ChangeUserRoleHandler : ICommandHandler<ChangeUserRoleCommand>
{
    private readonly IUserRepository _repository;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChangeUserRoleHandler> _logger;

    public ChangeUserRoleHandler(
        IUserRepository repository,
        IRefreshTokenRepository refreshTokens,
        TimeProvider timeProvider,
        ILogger<ChangeUserRoleHandler> logger)
    {
        _repository = repository;
        _refreshTokens = refreshTokens;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, меняет его роль и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ChangeUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var changed = user.ChangeRole(command.Role);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Старые refresh-токены несут прежнюю роль в claims следующего access-токена,
        // поэтому после смены роли все сессии пользователя должны перелогиниться.
        var revoked = await _refreshTokens.RevokeAllForUserAsync(
            user.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        _logger.LogInformation("User {UserId} role has been changed to {Role}.", user.Id, user.Role);

        return UnitResult.Success<Failure>();
    }
}
