using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens;
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
    private readonly IUserRepository _repository;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeactivateUserHandler> _logger;

    public DeactivateUserHandler(
        IUserRepository repository,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<DeactivateUserHandler> logger)
    {
        _repository = repository;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
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
