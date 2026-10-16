using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.ReorderPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="ReorderPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Фотография берётся из коллекции загруженного профиля, поэтому чужой порядок не изменить.
/// </remarks>
public class ReorderPortfolioPhotoHandler : ICommandHandler<ReorderPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<ReorderPortfolioPhotoHandler> _logger;

    public ReorderPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        ILogger<ReorderPortfolioPhotoHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Меняет порядок фотографии и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ReorderPortfolioPhotoCommand command,
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
                "portfolioPhoto.reorder.notFound",
                "The specified photo was not found in this artist's portfolio.",
                command.PhotoId));
        }

        var reordered = photo.Reorder(command.SortOrder);
        if (reordered.IsFailure)
        {
            return reordered.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Portfolio photo {PhotoId} has been reordered.", photo.Id);

        return UnitResult.Success<Failure>();
    }
}
