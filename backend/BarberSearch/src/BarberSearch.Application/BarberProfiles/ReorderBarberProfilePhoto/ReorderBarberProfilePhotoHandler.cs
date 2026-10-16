using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhoto;

/// <summary>
/// Обработчик команды <see cref="ReorderBarberProfilePhotoCommand"/>.
/// </summary>
public class ReorderBarberProfilePhotoHandler : ICommandHandler<ReorderBarberProfilePhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<ReorderBarberProfilePhotoHandler> _logger;

    public ReorderBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        ILogger<ReorderBarberProfilePhotoHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, меняет порядок фотографии и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ReorderBarberProfilePhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var reordered = profile.ReorderPhoto(command.PhotoId, command.SortOrder);
        if (reordered.IsFailure)
        {
            return reordered.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Photo {PhotoId} in barber profile {BarberProfileId} has been reordered.",
            command.PhotoId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
