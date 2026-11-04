using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberSalons.SetScheduleException;

/// <summary>
/// Обработчик команды <see cref="SetScheduleExceptionCommand"/>.
/// </summary>
/// <remarks>
/// Повторный вызов на ту же дату переписывает исключение, а не создаёт второе:
/// уникальность даты держит и домен, и индекс <c>ux_schedule_overrides_barber_salon_id_date</c>.
/// </remarks>
public class SetScheduleExceptionHandler
    : ICommandHandler<SetScheduleExceptionCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SetScheduleExceptionHandler> _logger;

    public SetScheduleExceptionHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<SetScheduleExceptionHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<UnitResult<Failure>> Handle(
        SetScheduleExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithScheduleAsync(
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

        var set = salon.SetScheduleException(
            command.Date,
            command.IsDayOff,
            command.StartTime,
            command.EndTime,
            command.Reason);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Schedule exception has been set for barber salon {SalonId} on {Date}.",
            salon.Id,
            command.Date);

        return UnitResult.Success<Failure>();
    }
}
