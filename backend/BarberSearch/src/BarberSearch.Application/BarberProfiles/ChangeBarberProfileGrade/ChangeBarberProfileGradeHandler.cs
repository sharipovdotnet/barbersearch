using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ChangeBarberProfileGrade;

/// <summary>
/// Обработчик команды <see cref="ChangeBarberProfileGradeCommand"/>.
/// </summary>
public class ChangeBarberProfileGradeHandler : ICommandHandler<ChangeBarberProfileGradeCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<ChangeBarberProfileGradeHandler> _logger;

    public ChangeBarberProfileGradeHandler(
        IBarberProfileRepository repository,
        ILogger<ChangeBarberProfileGradeHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера, меняет грейд и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ChangeBarberProfileGradeCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var changed = profile.ChangeGrade(command.Grade);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Grade of barber profile {BarberProfileId} has been changed.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
