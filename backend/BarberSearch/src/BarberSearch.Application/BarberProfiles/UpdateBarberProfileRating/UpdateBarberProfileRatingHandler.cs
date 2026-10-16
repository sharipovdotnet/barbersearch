using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfileRating;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberProfileRatingCommand"/>.
/// </summary>
public class UpdateBarberProfileRatingHandler : ICommandHandler<UpdateBarberProfileRatingCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<UpdateBarberProfileRatingHandler> _logger;

    public UpdateBarberProfileRatingHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberProfileRatingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, обновляет рейтинг и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberProfileRatingCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var updated = profile.UpdateRating(command.Rating, command.ReviewsCount);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Rating of barber profile {BarberProfileId} has been updated.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
