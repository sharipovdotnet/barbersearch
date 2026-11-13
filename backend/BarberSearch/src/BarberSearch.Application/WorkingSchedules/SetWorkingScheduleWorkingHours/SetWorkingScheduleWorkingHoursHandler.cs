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

namespace BarberSearch.Application.WorkingSchedules.SetWorkingScheduleWorkingHours;

/// <summary>
/// Обработчик команды <see cref="SetWorkingScheduleWorkingHoursCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Салон ищется в агрегате, расписание — в салоне: так чужой рабочий день не изменить.
/// </para>
/// <para>
/// Проверка конфликтов идёт только если часы реально меняются: повторная отправка тех же
/// значений не даёт <c>409</c>. Под блокировкой барбера, агрегат читается с исключениями
/// и отпусками, брони — уже после проверки прав. При <c>force</c> брони не читаются.
/// </para>
/// </remarks>
public class SetWorkingScheduleWorkingHoursHandler
    : ICommandHandler<ScheduleWriteOutcome, SetWorkingScheduleWorkingHoursCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IBookingRepository _bookings;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SetWorkingScheduleWorkingHoursHandler> _logger;

    public SetWorkingScheduleWorkingHoursHandler(
        IBarberProfileRepository repository,
        IBookingRepository bookings,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ILogger<SetWorkingScheduleWorkingHoursHandler> logger,
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
    /// Меняет рабочие часы расписания и сохраняет агрегат.
    /// </summary>
    public Task<Result<ScheduleWriteOutcome, Failure>> Handle(
        SetWorkingScheduleWorkingHoursCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteAsync(
            token => SetHoursAsync(command, token),
            cancellationToken);

    private async Task<Result<ScheduleWriteOutcome, Failure>> SetHoursAsync(
        SetWorkingScheduleWorkingHoursCommand command,
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

        // Исключения нужны, чтобы не проверять даты, перекрытые исключением.
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
                "workingSchedule.setWorkingHours.notFound",
                Messages.WORKING_SCHEDULE_NOT_FOUND,
                command.WorkingScheduleId));
        }

        // D4: проверяются только реально изменённые строки. Выходной при установке часов
        // всегда меняется: SetWorkingHours снимает флаг выходного.
        var changed = schedule.IsDayOff
                      || schedule.StartTime != command.StartTime
                      || schedule.EndTime != command.EndTime;

        // Невалидный интервал отклонит домен прежним 400: проверка конфликтов не должна
        // подменять ошибку запроса на 409.
        var validRange = command.EndTime > command.StartTime;

        if (!command.Force && changed && validRange)
        {
            var check = await CheckConflictsAsync(
                profile.Id,
                salon,
                schedule.DayOfWeek,
                command,
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

        var updated = schedule.SetWorkingHours(command.StartTime, command.EndTime);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Working schedule {WorkingScheduleId} hours have been updated.", schedule.Id);

        return ScheduleWriteOutcome.Saved;
    }

    private async Task<Result<ScheduleWriteOutcome, Failure>> CheckConflictsAsync(
        Guid barberProfileId,
        BarberSalon salon,
        DayOfWeek dayOfWeek,
        SetWorkingScheduleWorkingHoursCommand command,
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
            false,
            command.StartTime,
            command.EndTime,
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
