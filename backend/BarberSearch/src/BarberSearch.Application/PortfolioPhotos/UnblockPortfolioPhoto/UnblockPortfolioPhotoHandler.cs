using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.UnblockPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="UnblockPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Требует администратора до мутации: владелец профиля не может снять блокировку сам.
/// Фотография ищется в коллекции загруженного агрегата, поэтому чужая фотография
/// не изменится.
/// </remarks>
public class UnblockPortfolioPhotoHandler
    : ICommandHandler<UnblockPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UnblockPortfolioPhotoHandler> _logger;

    public UnblockPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<UnblockPortfolioPhotoHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Снимает блокировку и сохраняет агрегат. Повторное снятие идемпотентно.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UnblockPortfolioPhotoCommand command,
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
                "portfolioPhoto.unblock.notFound",
                DomainMessages.PHOTO_NOT_FOUND,
                command.PhotoId));
        }

        var unblocked = photo.Unblock();
        if (unblocked.IsFailure)
        {
            return unblocked.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Portfolio photo {PhotoId} of barber profile {BarberProfileId} has been unblocked.",
            photo.Id,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
