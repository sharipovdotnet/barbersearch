using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.PublishPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="PublishPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Фотография берётся из коллекции загруженного профиля, поэтому чужую фотографию не опубликовать.
/// </remarks>
public class PublishPortfolioPhotoHandler : ICommandHandler<PublishPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PublishPortfolioPhotoHandler> _logger;

    public PublishPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        ILogger<PublishPortfolioPhotoHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Публикует фотографию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        PublishPortfolioPhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
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
                "portfolioPhoto.publish.notFound",
                "The specified photo was not found in this artist's portfolio.",
                command.PhotoId));
        }

        var published = photo.Publish();
        if (published.IsFailure)
        {
            return published.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Portfolio photo {PhotoId} has been published.", photo.Id);

        return UnitResult.Success<Failure>();
    }
}
