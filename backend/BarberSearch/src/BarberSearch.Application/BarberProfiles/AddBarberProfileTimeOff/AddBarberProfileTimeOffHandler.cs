using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileTimeOffCommand"/>.
/// </summary>
public class AddBarberProfileTimeOffHandler : ICommandHandler<AddBarberProfileTimeOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileTimeOffHandler> _logger;

    public AddBarberProfileTimeOffHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberProfileTimeOffHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль барбера, добавляет нерабочий период и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileTimeOffCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(command.BarberProfileId, cancellationToken);
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

        var added = profile.AddTimeOff(command.StartAt, command.EndAt, command.Reason);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Time off has been added to barber profile {BarberProfileId}.", profile.Id);

        return UnitResult.Success<Failure>();
    }
}
