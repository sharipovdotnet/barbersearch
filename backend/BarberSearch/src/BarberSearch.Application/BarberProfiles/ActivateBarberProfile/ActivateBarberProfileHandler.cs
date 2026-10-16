using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ActivateBarberProfile;

/// <summary>
/// Обработчик команды <see cref="ActivateBarberProfileCommand"/>.
/// </summary>
public class ActivateBarberProfileHandler : ICommandHandler<ActivateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<ActivateBarberProfileHandler> _logger;

    public ActivateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<ActivateBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, активирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var activated = profile.Activate();
        if (activated.IsFailure)
        {
            return activated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been activated.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
