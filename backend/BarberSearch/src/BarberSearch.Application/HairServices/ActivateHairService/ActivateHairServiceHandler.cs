using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.HairServices.ActivateHairService;

/// <summary>
/// Обработчик команды <see cref="ActivateHairServiceCommand"/>.
/// </summary>
public class ActivateHairServiceHandler : ICommandHandler<ActivateHairServiceCommand>
{
    private readonly IHairServiceRepository _repository;
    private readonly ILogger<ActivateHairServiceHandler> _logger;

    public ActivateHairServiceHandler(
        IHairServiceRepository repository,
        ILogger<ActivateHairServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает услугу, активирует её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairServiceResult = await _repository.GetByIdAsync(command.HairServiceId, cancellationToken);
        if (hairServiceResult.IsFailure)
        {
            return hairServiceResult.Error;
        }

        var hairService = hairServiceResult.Value;

        var updated = hairService.Activate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(hairService, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been activated.", hairService.Id);

        return UnitResult.Success<Failure>();
    }
}
