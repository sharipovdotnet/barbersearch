using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.WorkingSchedules;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;
using BarberSalon = BarberSearch.Domain.BarberSalons.BarberSalon;

namespace BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;

/// <summary>
/// Обработчик команды <see cref="ReplaceWorkingSchedulesCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Салон ищется внутри загруженного агрегата — расписание нельзя привязать к чужому салону.
/// Замена идёт по дню недели: существующие строки обновляются, а не пересоздаются.
/// </para>
/// <para>
/// Проверка и запись атомарны: сначала берётся advisory-блокировка барбера, затем грузится
/// агрегат с исключениями и отпусками, и только после этого читаются брони. Проверяются
/// <b>только изменённые</b> дни недели: иначе «унаследованный» визит вне часов запретил бы
/// любую правку расписания навсегда. При <c>force</c> брони не читаются вовсе.
/// </para>
/// </remarks>
public class ReplaceWorkingSchedulesHandler
    : ICommandHandler<ScheduleWriteOutcome, ReplaceWorkingSchedulesCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IBookingRepository _bookings;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReplaceWorkingSchedulesHandler> _logger;

    public ReplaceWorkingSchedulesHandler(
        IBarberProfileRepository repository,
        IBookingRepository bookings,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ReplaceWorkingSchedulesHandler> logger)
    {
        _repository = repository;
        _bookings = bookings;
        _salons = salons;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<Result<ScheduleWriteOutcome, Failure>> Handle(
        ReplaceWorkingSchedulesCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteAsync(
            token => ReplaceAsync(command, token),
            cancellationToken);

    private async Task<Result<ScheduleWriteOutcome, Failure>> ReplaceAsync(
        ReplaceWorkingSchedulesCommand command,
        CancellationToken cancellationToken)
    {
        // Тот же advisory-ключ, что у создания брони: между проверкой и сохранением не может
        // вклиниться новая запись. Таймаут отдаёт свой код — barberSchedule.lockTimeout.
        var lockResult = await _bookings.AcquireBarberLockAsync(
            command.BarberProfileId,
            ApiErrorCodes.Schedule.LockTimeout,
            cancellationToken);
        if (lockResult.IsFailure)
        {
            return lockResult.Error;
        }

        // Исключения и отпуска нужны для перебора дат: GetWithSalonsAndSchedulesAsync их не грузит.
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

        if (!command.Force)
        {
            var check = await CheckConflictsAsync(salon, command, cancellationToken);
            if (check.IsFailure)
            {
                return check.Error;
            }

            if (check.Value.Conflicts.Count > 0)
            {
                return check.Value;
            }
        }

        var replaced = salon.ReplaceWorkingSchedules(command.Days);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var saved = await _repository.UpdateAsync(profile, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Weekly schedule has been replaced for barber salon {SalonId}.",
            salon.Id);

        return ScheduleWriteOutcome.Saved;
    }

    /// <summary>
    /// Проверяет только те дни, чьи эффективные часы реально меняются.
    /// </summary>
    /// <remarks>
    /// Невалидный набор дней пропускается: его отклонит домен прежним <c>400</c>, и проверка
    /// конфликтов не должна превращать ошибку запроса в <c>409</c>.
    /// </remarks>
    private async Task<Result<ScheduleWriteOutcome, Failure>> CheckConflictsAsync(
        BarberSalon salon,
        ReplaceWorkingSchedulesCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsWellFormed(command.Days))
        {
            return ScheduleWriteOutcome.Saved;
        }

        var changed = command.Days
            .Where(day => Differs(salon.WorkingSchedules, day))
            .ToList();

        if (changed.Count == 0)
        {
            return ScheduleWriteOutcome.Saved;
        }

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

        var windows = changed
            .SelectMany(day => ScheduleWindowFactory.ForWeekday(
                salon.SalonId,
                timeZoneId,
                day.DayOfWeek,
                day.IsDayOff,
                day.StartTime,
                day.EndTime,
                today,
                excluded))
            .ToList();

        return await ScheduleConflictCheck.FindOutsideHoursAsync(
            _bookings,
            command.BarberProfileId,
            windows,
            now,
            now.AddDays(Constants.SCHEDULE_CONFLICT_HORIZON_DAYS + 2),
            _logger,
            cancellationToken);
    }

    private static bool Differs(
        IReadOnlyCollection<WorkingSchedule> current,
        WorkingDaySchedule day)
    {
        var existing = current.FirstOrDefault(schedule => schedule.DayOfWeek == day.DayOfWeek);

        // Отсутствующая строка означает выходной: так же её читает экран расписания.
        var isDayOff = existing?.IsDayOff ?? true;
        var start = existing?.StartTime ?? TimeOnly.MinValue;
        var end = existing?.EndTime ?? TimeOnly.MinValue;

        var newStart = day.IsDayOff ? TimeOnly.MinValue : day.StartTime;
        var newEnd = day.IsDayOff ? TimeOnly.MinValue : day.EndTime;

        return isDayOff != day.IsDayOff || start != newStart || end != newEnd;
    }

    private static bool IsWellFormed(IReadOnlyList<WorkingDaySchedule> days)
        => days.Count == Constants.DAYS_IN_WEEK
           && days.Select(day => day.DayOfWeek).Distinct().Count() == Constants.DAYS_IN_WEEK
           && days.Where(day => !day.IsDayOff).All(day => day.EndTime > day.StartTime);
}
