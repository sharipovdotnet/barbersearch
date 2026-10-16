using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.HairServices.UpdateHairService;

/// <summary>
/// Обработчик команды <see cref="UpdateHairServiceCommand"/>.
/// </summary>
public class UpdateHairServiceHandler : ICommandHandler<UpdateHairServiceCommand>
{
    private readonly IHairServiceRepository _repository;
    private readonly ILogger<UpdateHairServiceHandler> _logger;

    public UpdateHairServiceHandler(
        IHairServiceRepository repository,
        ILogger<UpdateHairServiceHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает услугу, применяет к ней изменение и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateHairServiceCommand command,
        CancellationToken cancellationToken)
    {
        var hairServiceResult = await _repository.GetByIdAsync(command.HairServiceId, cancellationToken);
        if (hairServiceResult.IsFailure)
        {
            return hairServiceResult.Error;
        }

        var hairService = hairServiceResult.Value;

        var updated = hairService.Update(
            command.Name,
            command.Description,
            command.DefaultDurationMinutes);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(hairService, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Hair service {HairServiceId} has been updated.", hairService.Id);

        return UnitResult.Success<Failure>();
    }
}
