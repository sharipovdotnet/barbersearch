using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.HairServices.DeactivateHairService;

/// <summary>
/// Обработчик команды <see cref="DeactivateHairServiceCommand"/>.
/// </summary>
public class DeactivateHairServiceHandler : ICommandHandler<DeactivateHairServiceCommand>
{
    private readonly IHairServiceRepository _repository;
    private readonly ILogger<DeactivateHairServiceHandler> _logger;

    public DeactivateHairServiceHandler(
        IHairServiceRepository repository,
        ILogger<DeactivateHairServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает услугу, деактивирует её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairServiceResult = await _repository.GetByIdAsync(command.HairServiceId, cancellationToken);
        if (hairServiceResult.IsFailure)
        {
            return hairServiceResult.Error;
        }

        var hairService = hairServiceResult.Value;

        var updated = hairService.Deactivate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(hairService, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been deactivated.", hairService.Id);

        return UnitResult.Success<Failure>();
    }
}
