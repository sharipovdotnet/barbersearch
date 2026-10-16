using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileTimeOff;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberProfileTimeOffCommand"/>.
/// </summary>
public class RemoveBarberProfileTimeOffHandler : ICommandHandler<RemoveBarberProfileTimeOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<RemoveBarberProfileTimeOffHandler> _logger;

    public RemoveBarberProfileTimeOffHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileTimeOffHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, удаляет нерабочий период и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileTimeOffCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var removed = profile.RemoveTimeOff(command.TimeOffId);
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
            "Time off {TimeOffId} has been removed from barber profile {BarberProfileId}.",
            command.TimeOffId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
