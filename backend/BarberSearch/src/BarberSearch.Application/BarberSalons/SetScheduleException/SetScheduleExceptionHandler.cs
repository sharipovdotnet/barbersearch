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

namespace BarberSearch.Application.BarberSalons.SetScheduleException;

/// <summary>
/// Обработчик команды <see cref="SetScheduleExceptionCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Повторный вызов на ту же дату переписывает исключение, а не создаёт второе:
/// уникальность даты держит и домен, и индекс <c>ux_schedule_overrides_barber_salon_id_date</c>.
/// </para>
/// <para>
/// Явная дата проверяется на всю глубину, даже за горизонтом
/// <see cref="Constants.SCHEDULE_CONFLICT_HORIZON_DAYS"/>: запись на 75-й день не должна
/// проскочить через выходной, поставленный на эту дату. Переписывание исключения теми же
/// значениями конфликтом не считается. При <c>force</c> брони не читаются.
/// </para>
/// </remarks>
public class SetScheduleExceptionHandler
    : ICommandHandler<ScheduleWriteOutcome, SetScheduleExceptionCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IBookingRepository _bookings;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SetScheduleExceptionHandler> _logger;

    public SetScheduleExceptionHandler(
        IBarberProfileRepository repository,
        IBookingRepository bookings,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<SetScheduleExceptionHandler> logger)
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
        SetScheduleExceptionCommand command,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteAsync(
            token => SetExceptionAsync(command, token),
            cancellationToken);

    private async Task<Result<ScheduleWriteOutcome, Failure>> SetExceptionAsync(
        SetScheduleExceptionCommand command,
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

        if (!command.Force && IsChanged(salon, command) && IsWellFormed(command))
        {
            var check = await CheckConflictsAsync(
                profile.Id,
                salon,
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

        return ScheduleWriteOutcome.Saved;
    }

    /// <summary>
    /// Меняет ли команда эффективные часы даты. Сравнение — с текущим состоянием агрегата:
    /// повторная отправка тех же значений конфликтов не даёт.
    /// </summary>
    private static bool IsChanged(BarberSalon salon, SetScheduleExceptionCommand command)
    {
        var existing = salon.ScheduleExceptions
            .FirstOrDefault(exception => exception.Date == command.Date);

        if (existing is null)
        {
            return true;
        }

        var start = command.IsDayOff ? TimeOnly.MinValue : command.StartTime;
        var end = command.IsDayOff ? TimeOnly.MinValue : command.EndTime;

        return existing.IsDayOff != command.IsDayOff
               || existing.StartTime != start
               || existing.EndTime != end;
    }

    /// <summary>
    /// Проверяет только структурно корректную команду: её ошибки отклонит домен прежним 400,
    /// и проверка конфликтов не должна подменять их на 409.
    /// </summary>
    private static bool IsWellFormed(SetScheduleExceptionCommand command)
    {
        if (!command.IsDayOff && command.EndTime <= command.StartTime)
        {
            return false;
        }

        var reason = command.Reason?.Trim();

        return reason is null || reason.Length <= Constants.MAX_SCHEDULE_REASON_LENGTH;
    }

    private async Task<Result<ScheduleWriteOutcome, Failure>> CheckConflictsAsync(
        Guid barberProfileId,
        BarberSalon salon,
        SetScheduleExceptionCommand command,
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

        var window = ScheduleWindowFactory.ForDate(
            salon.SalonId,
            timeZoneId,
            command.Date,
            command.IsDayOff,
            command.StartTime,
            command.EndTime);

        // Явная дата проверяется независимо от горизонта: конец интервала чтения — за её
        // локальным днём, а не за 60-дневным окном повторяющихся дней.
        var daysAhead = Math.Max(command.Date.DayNumber - today.DayNumber, 0);

        return await ScheduleConflictCheck.FindOutsideHoursAsync(
            _bookings,
            barberProfileId,
            [window],
            now,
            now.AddDays(daysAhead + 2),
            _logger,
            cancellationToken);
    }
}
