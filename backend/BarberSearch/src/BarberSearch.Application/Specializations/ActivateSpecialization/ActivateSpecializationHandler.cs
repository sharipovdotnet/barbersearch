using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Specializations.ActivateSpecialization;

/// <summary>
/// Обработчик команды <see cref="ActivateSpecializationCommand"/>.
/// </summary>
public class ActivateSpecializationHandler : ICommandHandler<ActivateSpecializationCommand>
{
    private readonly ISpecializationRepository _repository;
    private readonly ILogger<ActivateSpecializationHandler> _logger;

    public ActivateSpecializationHandler(
        ISpecializationRepository repository,
        ILogger<ActivateSpecializationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает специализацию, активирует её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ActivateSpecializationCommand command,
        CancellationToken cancellationToken)
    {
        var specializationResult = await _repository.GetByIdAsync(
            command.SpecializationId,
            cancellationToken);
        if (specializationResult.IsFailure)
        {
            return specializationResult.Error;
        }

        var specialization = specializationResult.Value;

        var updated = specialization.Activate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(specialization, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Specialization {SpecializationId} has been activated.", specialization.Id);

        return UnitResult.Success<Failure>();
    }
}
