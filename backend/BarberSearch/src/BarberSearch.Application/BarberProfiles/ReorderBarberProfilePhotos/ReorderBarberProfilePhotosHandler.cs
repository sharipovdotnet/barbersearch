using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhotos;

/// <summary>
/// Обработчик команды <see cref="ReorderBarberProfilePhotosCommand"/>.
/// </summary>
/// <remarks>
/// Агрегат проверяет, что список — это ровно все фотографии профиля без дублей; при
/// расхождении возвращается <c>400 barberProfile.reorderPhotos.invalid</c> и порядок
/// не меняется.
/// </remarks>
public class ReorderBarberProfilePhotosHandler
    : ICommandHandler<ReorderBarberProfilePhotosCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReorderBarberProfilePhotosHandler> _logger;

    public ReorderBarberProfilePhotosHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<ReorderBarberProfilePhotosHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Присваивает фотографиям порядок из команды и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ReorderBarberProfilePhotosCommand command,
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

        var reordered = profile.ReorderPhotos(command.PhotoIds);
        if (reordered.IsFailure)
        {
            return reordered.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Portfolio photos of barber profile {BarberProfileId} have been reordered.",
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
