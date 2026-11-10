using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.BlockPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="BlockPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Требует администратора до мутации: владелец профиля не может заблокировать (или
/// разблокировать) фотографию сам. Фотография ищется в коллекции загруженного агрегата,
/// поэтому чужая фотография не изменится.
/// </remarks>
public class BlockPortfolioPhotoHandler
    : ICommandHandler<BlockPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BlockPortfolioPhotoHandler> _logger;

    public BlockPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<BlockPortfolioPhotoHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Блокирует фотографию и сохраняет агрегат. Повторная блокировка идемпотентна.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        BlockPortfolioPhotoCommand command,
        CancellationToken cancellationToken)
    {
        var allowed = _currentUser.EnsureAdmin();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var profileResult = await _repository.GetWithPhotosAsync(
            command.BarberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var photo = profile.PortfolioPhotos.FirstOrDefault(p => p.Id == command.PhotoId);
        if (photo is null)
        {
            return new Failure(Error.NotFound(
                "portfolioPhoto.block.notFound",
                DomainMessages.PHOTO_NOT_FOUND,
                command.PhotoId));
        }

        var blocked = photo.Block();
        if (blocked.IsFailure)
        {
            return blocked.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Portfolio photo {PhotoId} of barber profile {BarberProfileId} has been blocked.",
            photo.Id,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
