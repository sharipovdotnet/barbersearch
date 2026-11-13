using BarberSearch.Application.Common;
using BarberSearch.Domain;

namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Собирает окна проверки конфликтов из правки расписания.
/// </summary>
/// <remarks>
/// Повторяющийся день недели разворачивается в даты горизонта
/// <see cref="Constants.SCHEDULE_CONFLICT_HORIZON_DAYS"/> от «сегодня» в поясе салона; даты,
/// перекрытые исключением, из перебора выбрасываются — исключение важнее шаблона.
/// Явная дата исключения окно получает напрямую и на всю глубину: перебирать нечего.
/// </remarks>
public static class ScheduleWindowFactory
{
    /// <summary>
    /// Сегодняшняя дата в поясе салона.
    /// </summary>
    /// <remarks>
    /// Неразрешимый пояс не должен ронять проверку: берётся дата в UTC, а сам день всё равно
    /// не проверится — вычислитель пропустит окно с неразрешимым поясом и запишет <c>LogError</c>.
    /// </remarks>
    /// <param name="now">Текущий момент UTC.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    public static DateOnly Today(DateTimeOffset now, string timeZoneId)
    {
        if (SalonTimeZone.TryResolve(timeZoneId, out var zone))
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }

        return DateOnly.FromDateTime(now.UtcDateTime);
    }

    /// <summary>
    /// Все даты дня недели в горизонте, кроме дат с исключением.
    /// </summary>
    /// <param name="salonId">Салон (<c>Salon.Id</c>), к которому относится правка.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="dayOfWeek">Изменённый день недели.</param>
    /// <param name="isDayOff">Станет ли день выходным.</param>
    /// <param name="start">Новое начало дня; для выходного игнорируется.</param>
    /// <param name="end">Новое окончание дня; для выходного игнорируется.</param>
    /// <param name="today">Сегодняшняя дата в поясе салона.</param>
    /// <param name="excludedDates">Даты, перекрытые исключением.</param>
    public static IReadOnlyList<ScheduleDayWindow> ForWeekday(
        Guid salonId,
        string timeZoneId,
        DayOfWeek dayOfWeek,
        bool isDayOff,
        TimeOnly start,
        TimeOnly end,
        DateOnly today,
        IReadOnlySet<DateOnly> excludedDates)
    {
        ArgumentNullException.ThrowIfNull(excludedDates);

        var last = today.AddDays(Constants.SCHEDULE_CONFLICT_HORIZON_DAYS);
        var windows = new List<ScheduleDayWindow>();

        for (var date = today; date <= last; date = date.AddDays(1))
        {
            if (date.DayOfWeek != dayOfWeek || excludedDates.Contains(date))
            {
                continue;
            }

            windows.Add(ForDate(salonId, timeZoneId, date, isDayOff, start, end));
        }

        return windows;
    }

    /// <summary>
    /// Окно одной явной даты — без горизонта и без перебора.
    /// </summary>
    /// <param name="salonId">Салон (<c>Salon.Id</c>), к которому относится правка.</param>
    /// <param name="timeZoneId">Пояс салона.</param>
    /// <param name="date">Локальная дата.</param>
    /// <param name="isDayOff">Выходной ли это день.</param>
    /// <param name="start">Своё начало дня; для выходного игнорируется.</param>
    /// <param name="end">Своё окончание дня; для выходного игнорируется.</param>
    public static ScheduleDayWindow ForDate(
        Guid salonId,
        string timeZoneId,
        DateOnly date,
        bool isDayOff,
        TimeOnly start,
        TimeOnly end)
        => new(
            salonId,
            date,
            isDayOff,
            isDayOff ? TimeOnly.MinValue : start,
            isDayOff ? TimeOnly.MinValue : end,
            timeZoneId);
}
