using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.ActivateUser;

/// <summary>
/// Обработчик команды <see cref="ActivateUserCommand"/>.
/// </summary>
/// <remarks>
/// Повторная активация идемпотентна: доменный метод возвращает успех, ничего не меняя.
/// </remarks>
public class ActivateUserHandler : ICommandHandler<ActivateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<ActivateUserHandler> _logger;

    public ActivateUserHandler(IUserRepository repository, ILogger<ActivateUserHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, активирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateUserCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var activated = user.Activate();
        if (activated.IsFailure)
        {
            return activated.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} has been activated.", user.Id);

        return UnitResult.Success<Failure>();
    }
}
