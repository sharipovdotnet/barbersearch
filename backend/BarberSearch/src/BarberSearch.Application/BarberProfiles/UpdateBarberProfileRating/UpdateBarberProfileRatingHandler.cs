using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfileRating;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberProfileRatingCommand"/>.
/// </summary>
/// <remarks>
/// Административный ручной override. Читается только корень профиля, а не весь агрегат
/// с пятью коллекциями; row lock и транзакция не дают ручному изменению потеряться
/// относительно конкурентного пересчёта по отзывам (§12.2).
/// </remarks>
public class UpdateBarberProfileRatingHandler : ICommandHandler<UpdateBarberProfileRatingCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateBarberProfileRatingHandler> _logger;

    public UpdateBarberProfileRatingHandler(
        IBarberProfileRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateBarberProfileRatingHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Загружает корень профиля под row lock, обновляет рейтинг и сохраняет его.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberProfileRatingCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var profileResult = await _repository.GetForRatingUpdateAsync(
                command.BarberProfileId,
                token);
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

            return await _repository.UpdateAsync(profile, token);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Rating of barber profile {BarberProfileId} has been updated.",
                command.BarberProfileId);
        }

        return result;
    }
}
