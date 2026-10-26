using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;

/// <summary>
/// Обработчик команды <see cref="SetWorkingScheduleDayOffCommand"/>.
/// </summary>
/// <remarks>
/// Салон ищется в агрегате, расписание — в салоне: так чужой рабочий день не изменить.
/// </remarks>
public class SetWorkingScheduleDayOffHandler : ICommandHandler<SetWorkingScheduleDayOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SetWorkingScheduleDayOffHandler> _logger;

    public SetWorkingScheduleDayOffHandler(
        IBarberProfileRepository repository,
        ILogger<SetWorkingScheduleDayOffHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Помечает расписание выходным и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        SetWorkingScheduleDayOffCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithSalonsAndSchedulesAsync(
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

        var salonResult = profile.FindSalon(command.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var schedule = salon.WorkingSchedules
            .FirstOrDefault(s => s.Id == command.WorkingScheduleId);
        if (schedule is null)
        {
            return new Failure(Error.NotFound(
                "workingSchedule.setDayOff.notFound",
                "The specified working schedule was not found in this barber salon.",
                command.WorkingScheduleId));
        }

        var updated = schedule.SetDayOff();
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Working schedule {WorkingScheduleId} has been set as a day off.", schedule.Id);

        return UnitResult.Success<Failure>();
    }
}
