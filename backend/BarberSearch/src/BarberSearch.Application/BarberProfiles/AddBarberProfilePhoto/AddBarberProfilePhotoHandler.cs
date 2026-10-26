using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfilePhoto;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfilePhotoCommand"/>.
/// </summary>
/// <remarks>
/// Фотографию создаёт сам агрегат, поэтому прикрепить её к чужому профилю невозможно,
/// а ограничение на число фотографий проверяет домен.
/// </remarks>
public class AddBarberProfilePhotoHandler : ICommandHandler<AddBarberProfilePhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfilePhotoHandler> _logger;

    public AddBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfilePhotoHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с портфолио, добавляет фотографию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfilePhotoCommand command,
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

        var added = profile.AddPhoto(command.Url, command.ThumbnailUrl, command.Description);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Photo has been added to barber profile {BarberProfileId}.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
