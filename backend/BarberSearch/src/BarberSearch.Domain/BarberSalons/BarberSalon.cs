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

    public Guid BarberProfileId { get; private set; }
    public Guid SalonId { get; private set; }

    public bool IsPrimary { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    // Ссылки на Salon здесь нет — это граница другого агрегата.
    public BarberProfile BarberProfile { get; private set; } = null!;

    public IReadOnlyCollection<WorkingSchedule> WorkingSchedules
        => _workingSchedules.AsReadOnly();

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
