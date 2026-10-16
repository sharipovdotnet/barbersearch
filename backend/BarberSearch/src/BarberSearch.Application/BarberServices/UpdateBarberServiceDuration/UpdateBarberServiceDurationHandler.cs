using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberServices.UpdateBarberServiceDuration;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberServiceDurationCommand"/>.
/// </summary>
/// <remarks>
/// Услуга берётся из коллекции загруженного профиля, поэтому чужую услугу изменить нельзя.
/// </remarks>
public class UpdateBarberServiceDurationHandler : ICommandHandler<UpdateBarberServiceDurationCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<UpdateBarberServiceDurationHandler> _logger;

    public UpdateBarberServiceDurationHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberServiceDurationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Меняет длительность услуги и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberServiceDurationCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var service = profile.Services.FirstOrDefault(s => s.Id == command.BarberServiceId);
        if (service is null)
        {
            return new Failure(Error.NotFound(
                "barberService.updateDuration.notFound",
                "The specified service was not found in this barber profile.",
                command.BarberServiceId));
        }

        var updated = service.UpdateDuration(command.DurationMinutes);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber service {BarberServiceId} duration has been updated.", service.Id);

        return UnitResult.Success<Failure>();
    }
}
