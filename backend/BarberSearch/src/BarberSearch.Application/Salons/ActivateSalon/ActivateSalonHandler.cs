using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.ActivateSalon;

/// <summary>
/// Обработчик команды <see cref="ActivateSalonCommand"/>.
/// </summary>
public class ActivateSalonHandler : ICommandHandler<ActivateSalonCommand>
{
    private readonly ISalonRepository _repository;
    private readonly ILogger<ActivateSalonHandler> _logger;

    public ActivateSalonHandler(ISalonRepository repository, ILogger<ActivateSalonHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает салон, активирует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateSalonCommand command,
        CancellationToken cancellationToken)
    {
        var salonResult = await _repository.GetByIdAsync(command.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var updated = salon.Activate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(salon, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Salon {SalonId} has been activated.", salon.Id);

        return UnitResult.Success<Failure>();
    }
}
