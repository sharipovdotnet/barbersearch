using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.WorkingSchedules;

public class WorkingSchedule : Entity
{
    public Guid BarberProfileId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public bool IsDayOff { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;

    private WorkingSchedule()
    {
    }

    public WorkingSchedule(
        Guid barberProfileId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException(
                "End time must be after start time.");

        BarberProfileId = barberProfileId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;

        IsDayOff = false;
    }

    public void SetDayOff()
    {
        IsDayOff = true;
        MarkUpdated();
    }

    public void SetWorkingHours(
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException(
                "End time must be after start time.");

        StartTime = startTime;
        EndTime = endTime;
        IsDayOff = false;

        MarkUpdated();
    }
}