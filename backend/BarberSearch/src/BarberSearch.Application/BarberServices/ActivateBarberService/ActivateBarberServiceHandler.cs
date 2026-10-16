using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberServices.ActivateBarberService;

/// <summary>
/// Обработчик команды <see cref="ActivateBarberServiceCommand"/>.
/// </summary>
/// <remarks>
/// Услуга берётся из коллекции загруженного профиля, поэтому чужую услугу включить нельзя.
/// </remarks>
public class ActivateBarberServiceHandler : ICommandHandler<ActivateBarberServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<ActivateBarberServiceHandler> _logger;

    public ActivateBarberServiceHandler(
        IBarberProfileRepository repository,
        ILogger<ActivateBarberServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Включает услугу и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateBarberServiceCommand command,
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
                "barberService.activate.notFound",
                "The specified service was not found in this barber profile.",
                command.BarberServiceId));
        }

        var activated = service.Activate();
        if (activated.IsFailure)
        {
            return activated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber service {BarberServiceId} has been activated.", service.Id);

        return UnitResult.Success<Failure>();
    }
}
