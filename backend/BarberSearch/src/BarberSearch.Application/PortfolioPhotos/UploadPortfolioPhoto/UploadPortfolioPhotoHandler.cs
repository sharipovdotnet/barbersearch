using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.UploadPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="UploadPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// Порядок шагов важен: права проверяются до чтения файла, файл проверяется до записи,
/// лимит — до записи файла, а сохранение файла идёт до <c>SaveChanges</c>. Если после записи
/// файла падает домен или БД, файл удаляется компенсацией: строка в портфолио и файл
/// на диске не должны расходиться. Проигравшая гонку параллельная загрузка получает
/// <c>409</c>, а её файл-сирота тоже удаляется.
/// </remarks>
public class UploadPortfolioPhotoHandler
    : ICommandHandler<Guid, UploadPortfolioPhotoCommand>
{
    /// <summary>
    /// Код ошибки лимита портфолио: тот же, что у JSON-добавления фотографии.
    /// </summary>
    private const string PHOTO_LIMIT_CODE = "barberProfile.addPhoto.limit";

    private readonly IBarberProfileRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UploadPortfolioPhotoHandler> _logger;

    public UploadPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        IPhotoStorage storage,
        ICurrentUser currentUser,
        ILogger<UploadPortfolioPhotoHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет файл, сохраняет его, добавляет фотографию в агрегат и возвращает её
    /// идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        UploadPortfolioPhotoCommand command,
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

        // Владение проверяется до любой работы с файлом: чужой профиль не должен
        // приводить даже к чтению содержимого (и к записи на диск).
        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var inspection = ImageFileInspector.Inspect(command.Content);
        if (inspection.IsFailure)
        {
            return inspection.Error;
        }

        var info = inspection.Value;

        // Лимит проверяется до сохранения файла: 11-я фотография не оставляет сироту.
        // Домен проверит его ещё раз — на случай гонки двух загрузок.
        if (profile.PortfolioPhotos.Count >= Constants.MAX_PHOTOS_COUNT)
        {
            return new Failure(Error.Conflict(
                PHOTO_LIMIT_CODE,
                DomainMessages.PHOTO_LIMIT_REACHED));
        }

        var mediaId = Guid.CreateVersion7();

        var stored = await _storage.SaveAsync(
            profile.Id,
            mediaId,
            info.Extension,
            command.Content,
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        var storedPhoto = stored.Value;

        var added = profile.AddUploadedPhoto(storedPhoto.Url, command.Description);
        if (added.IsFailure)
        {
            await DeleteBestEffortAsync(profile.Id, storedPhoto.Url, cancellationToken);

            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            await DeleteBestEffortAsync(profile.Id, storedPhoto.Url, cancellationToken);

            return saved.Error;
        }

        var photo = profile.PortfolioPhotos.Single(p => p.Url == storedPhoto.Url);

        _logger.LogInformation(
            "Portfolio photo {PhotoId} has been uploaded to barber profile {BarberProfileId}: "
            + "{Size} bytes, {Width}x{Height}.",
            photo.Id,
            profile.Id,
            command.Content.Length,
            info.Width,
            info.Height);

        return photo.Id;
    }

    /// <summary>
    /// Удаляет файл, оставшийся без строки в БД. Ошибка удаления не подменяет исходную:
    /// строка уже не создана, а файл-сирота не ломает UX, поэтому в ответ уходит исходный
    /// <see cref="Failure"/>.
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
            // URL и путь к файлу в лог не попадают: они могут содержать идентификаторы,
            // которые не должны утекать в диагностику.
            _logger.LogWarning(
                exception,
                "Failed to delete an orphaned portfolio photo file of barber profile "
                + "{BarberProfileId}.",
                barberProfileId);
        }
    }
}
