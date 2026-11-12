using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;
using BarberSalon = BarberSearch.Domain.BarberSalons.BarberSalon;

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;

/// <summary>
/// Обработчик команды <see cref="SetWorkingScheduleDayOffCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Салон ищется в агрегате, расписание — в салоне: так чужой рабочий день не изменить.
/// </para>
/// <para>
/// Если день ещё не выходной, все даты этого дня недели в горизонте проверяются на активные
/// визиты. Уже выходной день не проверяется: повторная отправка ничего не ломает. При
/// <c>force</c> брони не читаются.
/// </para>
/// </remarks>
public class SetWorkingScheduleDayOffHandler
    : ICommandHandler<ScheduleWriteOutcome, SetWorkingScheduleDayOffCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IBookingRepository _bookings;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SetWorkingScheduleDayOffHandler> _logger;

    public SetWorkingScheduleDayOffHandler(
        IBarberProfileRepository repository,
        IBookingRepository bookings,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ILogger<SetWorkingScheduleDayOffHandler> logger,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _bookings = bookings;
        _salons = salons;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Помечает расписание выходным и сохраняет агрегат.
    /// </summary>
    public Task<Result<ScheduleWriteOutcome, Failure>> Handle(
        SetWorkingScheduleDayOffCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteAsync(
            token => SetDayOffAsync(command, token),
            cancellationToken);

    private async Task<Result<ScheduleWriteOutcome, Failure>> SetDayOffAsync(
        SetWorkingScheduleDayOffCommand command,
        CancellationToken cancellationToken)
    {
        var lockResult = await _bookings.AcquireBarberLockAsync(
            command.BarberProfileId,
            ApiErrorCodes.Schedule.LockTimeout,
            cancellationToken);
        if (lockResult.IsFailure)
        {
            return lockResult.Error;
        }

        // Исключения и отпуска нужны: даты, перекрытые исключением, из перебора выбрасываются.
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

        var schedule = salon.WorkingSchedules
            .FirstOrDefault(s => s.Id == command.WorkingScheduleId);
        if (schedule is null)
        {
            return new Failure(Error.NotFound(
                "workingSchedule.setDayOff.notFound",
                Messages.WORKING_SCHEDULE_NOT_FOUND,
                command.WorkingScheduleId));
        }

        var changed = !schedule.IsDayOff;

        if (!command.Force && changed)
        {
            var check = await CheckConflictsAsync(
                profile.Id,
                salon,
                schedule.DayOfWeek,
                cancellationToken);
            if (check.IsFailure)
            {
                return check.Error;
            }

            if (check.Value.Conflicts.Count > 0)
            {
                return check.Value;
            }
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

        return ScheduleWriteOutcome.Saved;
    }

    private async Task<Result<ScheduleWriteOutcome, Failure>> CheckConflictsAsync(
        Guid barberProfileId,
        BarberSalon salon,
        DayOfWeek dayOfWeek,
        CancellationToken cancellationToken)
    {
        var salonResult = await _salons.GetByIdAsync(salon.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var timeZoneId = salonResult.Value.TimeZoneId;
        var now = _timeProvider.GetUtcNow();
        var today = ScheduleWindowFactory.Today(now, timeZoneId);
        var excluded = salon.ScheduleExceptions
            .Select(exception => exception.Date)
            .ToHashSet();

        var windows = ScheduleWindowFactory.ForWeekday(
            salon.SalonId,
            timeZoneId,
            dayOfWeek,
            true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            today,
            excluded);

        return await ScheduleConflictCheck.FindOutsideHoursAsync(
            _bookings,
            barberProfileId,
            windows,
            now,
            now.AddDays(Constants.SCHEDULE_CONFLICT_HORIZON_DAYS + 2),
            _logger,
            cancellationToken);
    }
}
