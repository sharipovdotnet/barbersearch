using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateUserProfileHandler> _logger;

    public UpdateUserProfileHandler(
        IUserRepository repository,
        ICurrentUser currentUser,
        ILogger<UpdateUserProfileHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
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

        var allowed = _currentUser.EnsureCanManage(user.Id);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

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
