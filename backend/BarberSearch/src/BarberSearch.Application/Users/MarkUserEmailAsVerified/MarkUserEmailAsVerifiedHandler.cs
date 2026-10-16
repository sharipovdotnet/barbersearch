using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.MarkUserEmailAsVerified;

/// <summary>
/// Обработчик команды <see cref="MarkUserEmailAsVerifiedCommand"/>.
/// </summary>
public class MarkUserEmailAsVerifiedHandler : ICommandHandler<MarkUserEmailAsVerifiedCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<MarkUserEmailAsVerifiedHandler> _logger;

    public MarkUserEmailAsVerifiedHandler(IUserRepository repository, ILogger<MarkUserEmailAsVerifiedHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, помечает адрес электронной почты подтверждённым и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        MarkUserEmailAsVerifiedCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var verified = user.MarkEmailAsVerified();
        if (verified.IsFailure)
        {
            return verified.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} email has been marked as verified.", user.Id);

        return UnitResult.Success<Failure>();
    }
}
