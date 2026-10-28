using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Contracts.Common;
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
    /// <summary>
    /// Текст ошибки «нельзя менять роль себе».
    /// </summary>
    private const string SELF_CHANGE = "A user cannot change their own role.";

    /// <summary>
    /// Текст ошибки «нельзя оставить сервис без активного администратора».
    /// </summary>
    private const string LAST_ADMIN = "The service must keep at least one active administrator.";

    private readonly IUserRepository _repository;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChangeUserRoleHandler> _logger;

    public ChangeUserRoleHandler(
        IUserRepository repository,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ChangeUserRoleHandler> logger)
    {
        _repository = repository;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
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
        // Роль себе меняет другой администратор: иначе промах в интерфейсе оставляет
        // владельца без раздела, из которого он эту роль менял.
        if (_currentUser.TryGetUserId(out var actorId) && actorId == command.UserId)
        {
            return new Failure(Error.Conflict(ApiErrorCodes.User.RoleSelfChange, SELF_CHANGE));
        }

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
            // Блокировка сериализует смену ролей: без неё два администратора могли бы
            // одновременно понизить друг друга, оба увидеть ещё двоих и оставить сервис
            // без администратора.
            var locked = await _repository.AcquireAdminLockAsync(token);
            if (locked.IsFailure)
            {
                return locked.Error;
            }

            // Проверка касается только действующего администратора: клиента и барбера
            // можно понижать без ограничений.
            if (user.Role == UserRole.Admin && user.IsActive && command.Role != UserRole.Admin)
            {
                var admins = await _repository.CountActiveAdminsAsync(token);
                if (admins.IsFailure)
                {
                    return admins.Error;
                }

                if (admins.Value <= 1)
                {
                    return new Failure(Error.Conflict(
                        ApiErrorCodes.User.LastAdmin,
                        LAST_ADMIN));
                }
            }

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
