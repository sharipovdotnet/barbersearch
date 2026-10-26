using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Common;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdatePortfolioPhotoDescriptionHandler> _logger;

    public UpdatePortfolioPhotoDescriptionHandler(
        IBarberProfileRepository repository,
        ILogger<UpdatePortfolioPhotoDescriptionHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Меняет подпись к фотографии и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdatePortfolioPhotoDescriptionCommand command,
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

        var photo = profile.PortfolioPhotos.FirstOrDefault(p => p.Id == command.PhotoId);
        if (photo is null)
        {
            return new Failure(Error.NotFound(
                "portfolioPhoto.updateDescription.notFound",
                DomainMessages.PHOTO_NOT_FOUND,
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
