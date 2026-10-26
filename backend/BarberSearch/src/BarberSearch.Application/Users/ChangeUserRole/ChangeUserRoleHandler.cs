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
/// <para>
/// Новая роль и отзыв всех refresh-токенов пишутся в одной транзакции (R-08): иначе неудачный
/// отзыв оставил бы у пользователя живые сессии со старыми claims.
/// </para>
/// </remarks>
public class ChangeUserRoleHandler : ICommandHandler<ChangeUserRoleCommand>
{
    private readonly IUserRepository _repository;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChangeUserRoleHandler> _logger;

    public ChangeUserRoleHandler(
        IUserRepository repository,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ChangeUserRoleHandler> logger)
    {
        _repository = repository;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
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

        // Новая роль и отзыв старых сессий — одна транзакция: либо обе записи применились,
        // либо пользователь остаётся с прежней ролью и прежними токенами.
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var changed = user.ChangeRole(command.Role);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var saved = await _repository.UpdateAsync(user, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            // Старые refresh-токены несут прежнюю роль в claims следующего access-токена,
            // поэтому после смены роли все сессии пользователя должны перелогиниться.
            var revoked = await _refreshTokens.RevokeAllForUserAsync(
                user.Id,
                _timeProvider.GetUtcNow(),
                token);

            if (revoked.IsFailure)
            {
                return revoked.Error;
            }

            return UnitResult.Success<Failure>();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "User {UserId} role has been changed to {Role}.",
                user.Id,
                user.Role);
        }

        return result;
    }
}
