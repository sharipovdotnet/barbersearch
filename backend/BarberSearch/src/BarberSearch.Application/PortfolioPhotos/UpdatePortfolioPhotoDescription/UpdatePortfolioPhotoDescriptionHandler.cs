using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.UpdatePortfolioPhotoDescription;

/// <summary>
/// Обработчик команды <see cref="UpdatePortfolioPhotoDescriptionCommand"/>.
/// </summary>
/// <remarks>
/// Фотография берётся из коллекции загруженного профиля, поэтому чужую подпись не изменить.
/// </remarks>
public class UpdatePortfolioPhotoDescriptionHandler : ICommandHandler<UpdatePortfolioPhotoDescriptionCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<UpdatePortfolioPhotoDescriptionHandler> _logger;

    public UpdatePortfolioPhotoDescriptionHandler(
        IBarberProfileRepository repository,
        ILogger<UpdatePortfolioPhotoDescriptionHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Меняет подпись к фотографии и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdatePortfolioPhotoDescriptionCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var photo = profile.PortfolioPhotos.FirstOrDefault(p => p.Id == command.PhotoId);
        if (photo is null)
        {
            return new Failure(Error.NotFound(
                "portfolioPhoto.updateDescription.notFound",
                "The specified photo was not found in this artist's portfolio.",
                command.PhotoId));
        }

        var updated = photo.UpdateDescription(command.Description);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Portfolio photo {PhotoId} description has been updated.", photo.Id);

        return UnitResult.Success<Failure>();
    }
}
