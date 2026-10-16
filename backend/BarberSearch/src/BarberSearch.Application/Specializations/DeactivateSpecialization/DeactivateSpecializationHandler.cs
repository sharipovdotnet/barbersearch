using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Specializations.DeactivateSpecialization;

/// <summary>
/// Обработчик команды <see cref="DeactivateSpecializationCommand"/>.
/// </summary>
public class DeactivateSpecializationHandler : ICommandHandler<DeactivateSpecializationCommand>
{
    private readonly ISpecializationRepository _repository;
    private readonly ILogger<DeactivateSpecializationHandler> _logger;

    public DeactivateSpecializationHandler(
        ISpecializationRepository repository,
        ILogger<DeactivateSpecializationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает специализацию, деактивирует её и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeactivateSpecializationCommand command,
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

        var updated = specialization.Deactivate();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(specialization, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Specialization {SpecializationId} has been deactivated.", specialization.Id);

        return UnitResult.Success<Failure>();
    }
}
