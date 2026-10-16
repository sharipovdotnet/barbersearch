using BarberSearch.Application.Abstractions;
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
    private readonly ILogger<AddBarberProfilePhotoHandler> _logger;

    public AddBarberProfilePhotoHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfilePhotoHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, добавляет фотографию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfilePhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

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
