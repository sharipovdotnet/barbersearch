using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.DeactivateUser;

/// <summary>
/// Обработчик команды <see cref="DeactivateUserCommand"/>.
/// </summary>
/// <remarks>
/// Повторная деактивация идемпотентна: доменный метод возвращает успех, ничего не меняя.
/// <para>
/// Деактивация и отзыв всех refresh-токенов пишутся в одной транзакции (R-08): иначе неудачный
/// отзыв оставил бы активную учётную запись деактивированной, но с продлеваемой сессией.
/// </para>
/// </remarks>
public class DeactivateUserHandler : ICommandHandler<DeactivateUserCommand>
{
    /// <summary>
    /// Текст ошибки «нельзя заблокировать собственный доступ».
    /// </summary>
    private const string SELF_DEACTIVATION = "A user cannot deactivate their own access.";

    /// <summary>
    /// Текст ошибки «нельзя оставить сервис без активного администратора».
    /// </summary>
    private const string LAST_ADMIN = "The service must keep at least one active administrator.";

    private readonly IUserRepository _repository;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeactivateUserHandler> _logger;

    public DeactivateUserHandler(
        IUserRepository repository,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<DeactivateUserHandler> logger)
    {
        _repository = repository;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, деактивирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateUserCommand command,
        CancellationToken cancellationToken)
    {
        // Себя блокирует другой администратор: заблокированный не сможет вернуть доступ,
        // а восстановление возможно только этим же эндпоинтом.
        if (_currentUser.TryGetUserId(out var actorId) && actorId == command.UserId)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.User.DeactivationSelf,
                SELF_DEACTIVATION));
        }

        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        // Флаг активности и отзыв сессий — одна транзакция: деактивированный пользователь
        // не должен остаться с живым refresh-токеном.
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            // Та же блокировка, что и при смене роли: проверка «остался ли админ»
            // и запись деактивации должны быть атомарны.
            var locked = await _repository.AcquireAdminLockAsync(token);
            if (locked.IsFailure)
            {
                return locked.Error;
            }

            if (user.Role == UserRole.Admin && user.IsActive)
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

            var deactivated = user.Deactivate();
            if (deactivated.IsFailure)
            {
                return deactivated.Error;
            }

            var saved = await _repository.UpdateAsync(user, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            // Деактивированный пользователь не должен продлевать сессию живым refresh-токеном.
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
            _logger.LogInformation("User {UserId} has been deactivated.", user.Id);
        }

        return result;
    }
}
