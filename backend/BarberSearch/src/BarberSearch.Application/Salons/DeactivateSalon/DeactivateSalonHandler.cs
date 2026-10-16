using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.DeactivateSalon;

/// <summary>
/// Обработчик команды <see cref="DeactivateSalonCommand"/>.
/// </summary>
public class DeactivateSalonHandler : ICommandHandler<DeactivateSalonCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ILogger<DeactivateSalonHandler> _logger;

    public DeactivateSalonHandler(
        ISalonRepository repository,
        ILogger<DeactivateSalonHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, деактивирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateSalonCommand command,
        CancellationToken cancellationToken)
    {
        var salonResult = await _repository.GetByIdAsync(command.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var updated = salon.Deactivate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(salon, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Salon {SalonId} has been deactivated.", salon.Id);

        return UnitResult.Success<Failure>();
    }
}
