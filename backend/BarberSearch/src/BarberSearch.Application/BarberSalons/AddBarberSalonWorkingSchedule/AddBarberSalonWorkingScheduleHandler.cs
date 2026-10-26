using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.AddBarberSalonWorkingSchedule;

/// <summary>
/// Обработчик команды <see cref="AddBarberSalonWorkingScheduleCommand"/>.
/// </summary>
/// <remarks>
/// Салон ищется внутри загруженного агрегата — так расписание нельзя привязать к чужому салону.
/// </remarks>
public class AddBarberSalonWorkingScheduleHandler : ICommandHandler<AddBarberSalonWorkingScheduleCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberSalonWorkingScheduleHandler> _logger;

    public AddBarberSalonWorkingScheduleHandler(
        IBarberProfileRepository repository,
        ILogger<AddBarberSalonWorkingScheduleHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Добавляет рабочий день в расписание салона и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberSalonWorkingScheduleCommand command,
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

        var added = salon.AddWorkingSchedule(
            command.DayOfWeek,
            command.StartTime,
            command.EndTime);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Working schedule {DayOfWeek} has been added to barber salon {SalonId}.",
            command.DayOfWeek,
            salon.Id);

        return UnitResult.Success<Failure>();
    }
}
