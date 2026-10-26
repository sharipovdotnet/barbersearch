using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberServices.UpdateBarberServicePrice;

/// <summary>
/// Обработчик команды <see cref="UpdateBarberServicePriceCommand"/>.
/// </summary>
/// <remarks>
/// Услуга берётся из коллекции загруженного профиля, поэтому чужую услугу изменить нельзя.
/// </remarks>
public class UpdateBarberServicePriceHandler : ICommandHandler<UpdateBarberServicePriceCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateBarberServicePriceHandler> _logger;

    public UpdateBarberServicePriceHandler(
        IBarberProfileRepository repository,
        ILogger<UpdateBarberServicePriceHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Меняет цену услуги и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateBarberServicePriceCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithServicesAsync(
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

        var service = profile.Services.FirstOrDefault(s => s.Id == command.BarberServiceId);
        if (service is null)
        {
            return new Failure(Error.NotFound(
                "barberService.updatePrice.notFound",
                Messages.SERVICE_NOT_FOUND_IN_PROFILE,
                command.BarberServiceId));
        }

        var updated = service.UpdatePrice(command.Price);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Barber service {BarberServiceId} price has been updated.", service.Id);

        return UnitResult.Success<Failure>();
    }
}
