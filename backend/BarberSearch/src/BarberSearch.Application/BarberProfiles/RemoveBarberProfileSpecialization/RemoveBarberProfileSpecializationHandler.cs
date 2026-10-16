using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileSpecialization;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfileSpecializationCommand"/>.
/// </summary>
public class RemoveBarberProfileSpecializationHandler
    : ICommandHandler<RemoveBarberProfileSpecializationCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<RemoveBarberProfileSpecializationHandler> _logger;

    public RemoveBarberProfileSpecializationHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileSpecializationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, удаляет специализацию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileSpecializationCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var removed = profile.RemoveSpecialization(command.SpecializationId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Specialization {SpecializationId} has been removed from barber profile {BarberProfileId}.",
            command.SpecializationId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
