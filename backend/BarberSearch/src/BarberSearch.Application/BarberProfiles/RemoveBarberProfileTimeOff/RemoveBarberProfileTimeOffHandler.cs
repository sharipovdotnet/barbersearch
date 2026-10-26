using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberProfileTimeOffHandler> _logger;

    public RemoveBarberProfileTimeOffHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberProfileTimeOffHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль с отпусками, удаляет нерабочий период и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberProfileTimeOffCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithTimeOffsAsync(
            command.BarberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

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
