using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfilePhotoCommand"/>.
/// </summary>
/// <remarks>
/// После успешного сохранения файл удаляется best-effort: строка в портфолио уже удалена,
/// а раздача ответит <c>404</c> по проверке БД, поэтому сбой удаления не превращает
/// <c>204</c> в <c>500</c> — в лог пишется <c>Warning</c>.
/// </remarks>
public class RemoveBarberProfilePhotoHandler : ICommandHandler<RemoveBarberProfilePhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfilePhotoHandler> _logger;

    public RemoveBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        IPhotoStorage storage,
        ICurrentUser currentUser,
        ILogger<RemoveBarberProfilePhotoHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль с портфолио, удаляет фотографию, сохраняет агрегат и удаляет файл.
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

        // URL читается до удаления: после RemovePhoto объекта в коллекции уже нет.
        var photoUrl = profile.PortfolioPhotos
            .FirstOrDefault(photo => photo.Id == command.PhotoId)
            ?.Url;

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

        if (photoUrl is not null)
        {
            await DeleteBestEffortAsync(profile.Id, photoUrl, cancellationToken);
        }

        return UnitResult.Success<Failure>();
    }

    /// <summary>
    /// Удаляет файл фотографии. Внешние URL хранилище игнорирует; ошибка удаления —
    /// только <c>Warning</c>: строка уже удалена.
    /// </summary>
    private async Task DeleteBestEffortAsync(
        Guid barberProfileId,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(url, cancellationToken);
        }
        catch (Exception exception)
        {
            // URL и путь к файлу в лог не попадают.
            _logger.LogWarning(
                exception,
                "Failed to delete the file of a removed portfolio photo of barber profile "
                + "{BarberProfileId}.",
                barberProfileId);
        }
    }
}
