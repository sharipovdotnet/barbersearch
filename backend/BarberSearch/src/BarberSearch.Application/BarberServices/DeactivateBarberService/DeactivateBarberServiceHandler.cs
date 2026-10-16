using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberServices.DeactivateBarberService;

/// <summary>
/// Обработчик команды <see cref="DeactivateBarberServiceCommand"/>.
/// </summary>
/// <remarks>
/// Услуга берётся из коллекции загруженного профиля, поэтому чужую услугу выключить нельзя.
/// </remarks>
public class DeactivateBarberServiceHandler : ICommandHandler<DeactivateBarberServiceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<DeactivateBarberServiceHandler> _logger;

    public DeactivateBarberServiceHandler(
        IBarberProfileRepository repository,
        ILogger<DeactivateBarberServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Выключает услугу и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateBarberServiceCommand command,
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
                "barberService.deactivate.notFound",
                "The specified service was not found in this barber profile.",
                command.BarberServiceId));
        }

        var deactivated = service.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber service {BarberServiceId} has been deactivated.", service.Id);

        return UnitResult.Success<Failure>();
    }
}
