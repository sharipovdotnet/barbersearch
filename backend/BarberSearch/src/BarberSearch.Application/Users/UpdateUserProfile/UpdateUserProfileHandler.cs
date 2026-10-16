using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.UpdateUserProfile;

/// <summary>
/// Обработчик команды <see cref="UpdateUserProfileCommand"/>.
/// </summary>
public class UpdateUserProfileHandler : ICommandHandler<UpdateUserProfileCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<UpdateUserProfileHandler> _logger;

    public UpdateUserProfileHandler(IUserRepository repository, ILogger<UpdateUserProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, применяет к нему изменение и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateUserProfileCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var updated = user.UpdateProfile(
            command.FirstName,
            command.LastName,
            command.PhoneNumber,
            command.AvatarUrl);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("User {UserId} profile has been updated.", user.Id);

        return UnitResult.Success<Failure>();
    }
}
