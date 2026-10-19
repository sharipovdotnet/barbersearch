using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.UnpublishPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="UnpublishPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Фотография берётся из коллекции загруженного профиля, поэтому чужую фотографию не снять с публикации.
/// </remarks>
public class UnpublishPortfolioPhotoHandler : ICommandHandler<UnpublishPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UnpublishPortfolioPhotoHandler> _logger;

    public UnpublishPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        ILogger<UnpublishPortfolioPhotoHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Снимает фотографию с публикации и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UnpublishPortfolioPhotoCommand command,
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
                "portfolioPhoto.unpublish.notFound",
                "The specified photo was not found in this artist's portfolio.",
                command.PhotoId));
        }

        var unpublished = photo.Unpublish();
        if (unpublished.IsFailure)
        {
            return unpublished.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Portfolio photo {PhotoId} has been unpublished.", photo.Id);

        return UnitResult.Success<Failure>();
    }
}
