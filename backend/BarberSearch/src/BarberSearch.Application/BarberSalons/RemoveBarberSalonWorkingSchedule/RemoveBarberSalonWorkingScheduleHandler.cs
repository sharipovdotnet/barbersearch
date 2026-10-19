using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.RemoveBarberSalonWorkingSchedule;

/// <summary>
/// Обработчик команды <see cref="RemoveBarberSalonWorkingScheduleCommand"/>.
/// </summary>
/// <remarks>
/// Салон ищется внутри загруженного агрегата — так из чужого салона расписание не удалить.
/// </remarks>
public class RemoveBarberSalonWorkingScheduleHandler : ICommandHandler<RemoveBarberSalonWorkingScheduleCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RemoveBarberSalonWorkingScheduleHandler> _logger;

    public RemoveBarberSalonWorkingScheduleHandler(
        IBarberProfileRepository repository,
        ILogger<RemoveBarberSalonWorkingScheduleHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Удаляет запись расписания из салона и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RemoveBarberSalonWorkingScheduleCommand command,
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

        var salonResult = profile.FindSalon(command.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var salon = salonResult.Value;

        var removed = salon.RemoveWorkingSchedule(command.WorkingScheduleId);
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
            "Working schedule {WorkingScheduleId} has been removed from barber salon {SalonId}.",
            command.WorkingScheduleId,
            salon.Id);

        return UnitResult.Success<Failure>();
    }
}
