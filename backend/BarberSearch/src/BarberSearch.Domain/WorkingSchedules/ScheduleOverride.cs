using BarberSearch.Domain.BarberSalons;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.WorkingSchedules;

/// <summary>
/// Исключение из недельного шаблона на конкретную дату: выходной или свои часы.
/// Дочерняя сущность агрегата <see cref="BarberSalon"/>.
/// </summary>
/// <remarks>
/// Недельный шаблон отвечает на вопрос «как обычно», исключение — «что не так в этот день».
/// Отпуск (<c>TimeOff</c>) отличается тем, что задаёт диапазон и принадлежит профилю,
/// а не паре (барбер, салон).
/// <para>
/// Тип назван <c>ScheduleOverride</c>, а не <c>ScheduleException</c>: анализатор CA1711
/// запрещает имена, оканчивающиеся на «Exception», — иначе тип путают с классом ошибки.
/// В API и интерфейсе сущность по-прежнему называется «исключением».
/// </para>
/// </remarks>
public class ScheduleOverride : Entity
{
    public Guid BarberSalonId { get; private set; }

    public DateOnly Date { get; private set; }

    public bool IsDayOff { get; private set; }

    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public string? Reason { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberSalon BarberSalon { get; private set; } = null!;

    private ScheduleOverride()
    {
    }

    private ScheduleOverride(
        Guid barberSalonId,
        DateOnly date,
        bool isDayOff,
        TimeOnly startTime,
        TimeOnly endTime,
        string? reason)
    {
        BarberSalonId = barberSalonId;
        Date = date;
        IsDayOff = isDayOff;
        StartTime = startTime;
        EndTime = endTime;
        Reason = reason;
    }

    /// <summary>
    /// Создаёт исключение от имени агрегата <c>BarberSalon</c>.
    /// </summary>
    internal static ScheduleOverride Create(
        Guid barberSalonId,
        DateOnly date,
        bool isDayOff,
        TimeOnly startTime,
        TimeOnly endTime,
        string? reason)
        => new(barberSalonId, date, isDayOff, startTime, endTime, reason);

    /// <summary>
    /// Переписывает исключение на ту же дату. Границы проверяет агрегат.
    /// </summary>
    internal void Update(
        bool isDayOff,
        TimeOnly startTime,
        TimeOnly endTime,
        string? reason)
    {
        IsDayOff = isDayOff;
        StartTime = startTime;
        EndTime = endTime;
        Reason = reason;

        MarkUpdated();
    }
}
