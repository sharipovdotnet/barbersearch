using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.WorkingSchedules;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.BarberSalons;

/// <summary>
/// Салоны, в которых работает барбер. Дочерняя сущность агрегата <see cref="BarberProfile"/>:
/// создаётся только методами агрегата, поэтому «чужого» ребёнка прикрепить нельзя.
/// </summary>
/// <remarks>
/// Расписание хранится здесь, а не в <see cref="BarberProfile"/>: слоты считаются
/// по паре (барбер, салон).
/// </remarks>
public class BarberSalon : Entity
{
    private readonly List<WorkingSchedule> _workingSchedules = [];
    private readonly List<ScheduleOverride> _scheduleExceptions = [];

    public Guid BarberProfileId { get; private set; }
    public Guid SalonId { get; private set; }

    public bool IsPrimary { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    // Ссылки на Salon здесь нет — это граница другого агрегата.
    public BarberProfile BarberProfile { get; private set; } = null!;

    public IReadOnlyCollection<WorkingSchedule> WorkingSchedules
        => _workingSchedules.AsReadOnly();

    /// <summary>
    /// Исключения на конкретные даты: выходные и особые часы.
    /// </summary>
    public IReadOnlyCollection<ScheduleOverride> ScheduleExceptions
        => _scheduleExceptions.AsReadOnly();

    private BarberSalon()
    {
    }

    private BarberSalon(Guid barberProfileId, Guid salonId, bool isPrimary)
    {
        BarberProfileId = barberProfileId;
        SalonId = salonId;
        IsPrimary = isPrimary;
    }

    /// <summary>
    /// Создаёт связь от имени агрегата <c>BarberProfile</c>.
    /// </summary>
    internal static BarberSalon Create(Guid barberProfileId, Guid salonId, bool isPrimary)
        => new(barberProfileId, salonId, isPrimary);

    /// <summary>
    /// Делает салон основным. Снятие флага с остальных салонов — забота агрегата
    /// (<c>BarberProfile.SetPrimarySalon</c>), сам по себе этот метод флаг не снимает.
    /// </summary>
    internal void SetAsPrimary()
    {
        IsPrimary = true;
        MarkUpdated();
    }

    /// <summary>
    /// Снимает флаг основного салона.
    /// </summary>
    internal void SetAsSecondary()
    {
        IsPrimary = false;
        MarkUpdated();
    }

    /// <summary>
    /// Добавляет рабочий день. Дубликат по <see cref="DayOfWeek"/> не допускается.
    /// </summary>
    public UnitResult<Failure> AddWorkingSchedule(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        var errors = DomainErrors.Collect();

        if (endTime <= startTime)
        {
            errors.Add(Error.Validation(
                "barberSalon.addWorkingSchedule.timeRange",
                DomainMessages.WORKING_SCHEDULE_END_TIME_NOT_AFTER_START,
                nameof(endTime)));
        }

        if (DuplicateDayError(dayOfWeek) is { } duplicateError)
        {
            errors.Add(duplicateError);
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        _workingSchedules.Add(
            WorkingSchedule.Create(Id, dayOfWeek, startTime, endTime, isDayOff: false));

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Помечает день недели нерабочим. Время в этом случае не задаётся.
    /// </summary>
    public UnitResult<Failure> AddDayOff(DayOfWeek dayOfWeek)
    {
        if (DuplicateDayError(dayOfWeek) is { } duplicateError)
        {
            return new Failure(duplicateError);
        }

        _workingSchedules.Add(
            WorkingSchedule.Create(Id, dayOfWeek, TimeOnly.MinValue, TimeOnly.MinValue, isDayOff: true));

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Заменяет недельный шаблон целиком: семь дней, у каждого — рабочие часы или выходной.
    /// </summary>
    /// <remarks>
    /// Существующие строки обновляются по дню недели, а не удаляются и создаются заново:
    /// идентификаторы расписания остаются стабильными, а EF Core не пытается вставить
    /// новую строку раньше, чем удалить старую с тем же уникальным днём.
    /// </remarks>
    public UnitResult<Failure> ReplaceWorkingSchedules(
        IReadOnlyCollection<WorkingDaySchedule> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var errors = DomainErrors.Collect();

        if (days.Count != Constants.DAYS_IN_WEEK)
        {
            errors.Add(Error.Validation(
                "barberSalon.replaceSchedule.days",
                $"A weekly schedule must contain exactly {Constants.DAYS_IN_WEEK} days.",
                nameof(days)));
        }

        if (days
            .GroupBy(day => day.DayOfWeek)
            .Any(group => group.Count() > 1))
        {
            errors.Add(Error.Validation(
                "barberSalon.replaceSchedule.duplicateDay",
                "A weekly schedule cannot contain the same day of week twice.",
                nameof(days)));
        }

        foreach (var day in days.Where(day => !day.IsDayOff))
        {
            if (day.EndTime <= day.StartTime)
            {
                errors.Add(Error.Validation(
                    "barberSalon.replaceSchedule.timeRange",
                    DomainMessages.WORKING_SCHEDULE_END_TIME_NOT_AFTER_START,
                    nameof(day.EndTime)));
            }
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        foreach (var day in days)
        {
            var existing = _workingSchedules
                .FirstOrDefault(schedule => schedule.DayOfWeek == day.DayOfWeek);

            if (existing is null)
            {
                _workingSchedules.Add(WorkingSchedule.Create(
                    Id,
                    day.DayOfWeek,
                    day.IsDayOff ? TimeOnly.MinValue : day.StartTime,
                    day.IsDayOff ? TimeOnly.MinValue : day.EndTime,
                    day.IsDayOff));
            }
            else if (day.IsDayOff)
            {
                existing.SetDayOff();
            }
            else
            {
                existing.SetWorkingHours(day.StartTime, day.EndTime);
            }
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Ставит исключение на дату: выходной или свои часы. Повторный вызов переписывает его.
    /// </summary>
    public UnitResult<Failure> SetScheduleException(
        DateOnly date,
        bool isDayOff,
        TimeOnly startTime,
        TimeOnly endTime,
        string? reason = null)
    {
        if (!isDayOff && endTime <= startTime)
        {
            return new Failure(Error.Validation(
                "barberSalon.setScheduleException.timeRange",
                DomainMessages.WORKING_SCHEDULE_END_TIME_NOT_AFTER_START,
                nameof(endTime)));
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (normalizedReason is not null
            && normalizedReason.Length > Constants.MAX_SCHEDULE_REASON_LENGTH)
        {
            return new Failure(Error.Validation(
                "barberSalon.setScheduleException.reasonTooLong",
                $"A schedule exception reason cannot be longer than {Constants.MAX_SCHEDULE_REASON_LENGTH} characters.",
                nameof(reason)));
        }

        var existing = _scheduleExceptions.FirstOrDefault(exception => exception.Date == date);

        if (existing is null)
        {
            _scheduleExceptions.Add(ScheduleOverride.Create(
                Id,
                date,
                isDayOff,
                isDayOff ? TimeOnly.MinValue : startTime,
                isDayOff ? TimeOnly.MinValue : endTime,
                normalizedReason));
        }
        else
        {
            existing.Update(
                isDayOff,
                isDayOff ? TimeOnly.MinValue : startTime,
                isDayOff ? TimeOnly.MinValue : endTime,
                normalizedReason);
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Снимает исключение с даты. Отсутствующее исключение — <c>404</c>.
    /// </summary>
    public UnitResult<Failure> RemoveScheduleException(DateOnly date)
    {
        var removed = _scheduleExceptions.RemoveAll(exception => exception.Date == date);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "barberSalon.removeScheduleException.notFound",
                $"The schedule exception was not found on date '{date}'."));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> RemoveWorkingSchedule(Guid workingScheduleId)
    {
        var removed = _workingSchedules.RemoveAll(s => s.Id == workingScheduleId);

        if (removed == 0)
        {
            return new Failure(Error.NotFound(
                "barberSalon.removeWorkingSchedule.notFound",
                "The working schedule was not found in this barber salon.",
                workingScheduleId));
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    private Error? DuplicateDayError(DayOfWeek dayOfWeek)
        => _workingSchedules.Any(s => s.DayOfWeek == dayOfWeek)
            ? Error.Conflict(
                "barberSalon.addWorkingSchedule.duplicateDay",
                $"A working schedule for {dayOfWeek} already exists in this barber salon.")
            : null;
}
