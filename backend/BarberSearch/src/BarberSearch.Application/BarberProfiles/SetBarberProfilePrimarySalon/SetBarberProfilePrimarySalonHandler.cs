using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.SetBarberProfilePrimarySalon;

/// <summary>
/// Обработчик команды <see cref="SetBarberProfilePrimarySalonCommand"/>.
/// </summary>
public class SetBarberProfilePrimarySalonHandler
    : ICommandHandler<SetBarberProfilePrimarySalonCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<SetBarberProfilePrimarySalonHandler> _logger;

    public SetBarberProfilePrimarySalonHandler(
        IBarberProfileRepository repository,
        ILogger<SetBarberProfilePrimarySalonHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, назначает основной салон и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        SetBarberProfilePrimarySalonCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var changed = profile.SetPrimarySalon(command.SalonId);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been set as primary for barber profile {BarberProfileId}.",
            command.SalonId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
