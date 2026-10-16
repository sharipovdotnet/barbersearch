using BarberSearch.Application.Abstractions;
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
    private readonly ILogger<RemoveBarberProfilePhotoHandler> _logger;

    public RemoveBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfilePhotoHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, удаляет фотографию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfilePhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

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
