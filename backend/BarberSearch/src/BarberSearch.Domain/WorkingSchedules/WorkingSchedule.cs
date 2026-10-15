using System;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.WorkingSchedules;

/// <summary>
/// Рабочее расписание салона: день недели и интервал. Дочерняя сущность агрегата
/// <see cref="BarberSalon"/> — расписание принадлежит паре (барбер, салон),
/// иначе при работе в нескольких салонах расписания неразличимы.
/// </summary>
public class WorkingSchedule : Entity
{
    public Guid BarberSalonId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public bool IsDayOff { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberSalon BarberSalon { get; private set; } = null!;

    private WorkingSchedule()
    {
    }

    private WorkingSchedule(
        Guid barberSalonId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isDayOff)
    {
        BarberSalonId = barberSalonId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        IsDayOff = isDayOff;
    }

    /// <summary>
    /// Создаёт расписание от имени агрегата <c>BarberSalon</c>.
    /// </summary>
    internal static WorkingSchedule Create(
        Guid barberSalonId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isDayOff)
        => new(barberSalonId, dayOfWeek, startTime, endTime, isDayOff);

    public UnitResult<Failure> SetDayOff()
    {
        IsDayOff = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> SetWorkingHours(TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            return DomainErrors.Fail(Error.Validation(
                "workingSchedule.setWorkingHours.timeRange",
                "Working schedule end time must be after its start time.",
                nameof(endTime)));
        }

        StartTime = startTime;
        EndTime = endTime;
        IsDayOff = false;

        MarkUpdated();

        return DomainErrors.Ok();
    }
}
