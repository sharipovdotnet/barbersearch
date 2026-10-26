using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfilePhotoCommand"/>.
/// </summary>
public class RemoveBarberProfilePhotoHandler : ICommandHandler<RemoveBarberProfilePhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfilePhotoHandler> _logger;

    public RemoveBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfilePhotoHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с портфолио, удаляет фотографию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfilePhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithPhotosAsync(
            command.BarberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var removed = profile.RemovePhoto(command.PhotoId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Photo {PhotoId} has been removed from barber profile {BarberProfileId}.",
            command.PhotoId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
