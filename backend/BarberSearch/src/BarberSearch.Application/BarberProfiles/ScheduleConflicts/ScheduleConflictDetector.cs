using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Чистый вычислитель конфликтов расписания: никакой БД и никакого <c>async</c> внутри.
/// </summary>
/// <remarks>
/// <para>
/// Вся арифметика времени и поясов — в C#, через <see cref="SalonTimeZone"/>: у Npgsql 10 нет
/// публичного <c>EF.Functions.AtTimeZone</c>, а <c>AT TIME ZONE</c> по строке салона падает на
/// исторических Windows-идентификаторах зон. Поэтому окно дня переводится в UTC здесь, а не
/// в SQL.
/// </para>
/// <para>
/// Касание границ конфликтом не считается: визит, который заканчивается ровно в момент
/// закрытия, — норма. Записи вне окна (другой салон, другой локальный день) не проверяются.
/// </para>
/// </remarks>
public static class ScheduleConflictDetector
{
    /// <summary>
    /// Записи, которые после правки не помещаются в эффективные часы своего локального дня.
    /// </summary>
    /// <remarks>
    /// День-выходной делает затронутой любую активную запись этой даты. Для рабочего дня
    /// запись помещается, если <c>StartAt &gt;= dayStart</c> и <c>EndAt &lt;= dayEnd</c>.
    /// День, у которого пояс не разрешился или локальная граница попала в дыру перехода
    /// на летнее время, не проверяется: сначала пишется <c>LogError</c>/<c>LogWarning</c>,
    /// а блокировать сохранение из-за несуществующего часа нельзя.
    /// </remarks>
    /// <param name="windows">Эффективные окна изменённых дней.</param>
    /// <param name="bookings">Активные брони в проверяемом интервале.</param>
    /// <param name="logger">
    /// Журнал вызывающего обработчика. Необязателен, чтобы функцию можно было покрыть
    /// unit-тестом без DI; по умолчанию — <see cref="NullLogger.Instance"/>.
    /// </param>
    /// <returns>Затронутые записи, по <c>StartAt</c> возрастанию, затем по идентификатору.</returns>
    public static IReadOnlyList<ScheduleConflictResponse> FindOutsideHours(
        IReadOnlyList<ScheduleDayWindow> windows,
        IReadOnlyList<ScheduleConflictBooking> bookings,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(bookings);

        if (windows.Count == 0 || bookings.Count == 0)
        {
            return [];
        }

        var log = logger ?? NullLogger.Instance;
        var zones = new Dictionary<string, TimeZoneInfo?>(StringComparer.Ordinal);
        var unresolvedZones = new HashSet<string>(StringComparer.Ordinal);
        var windowsByKey = new Dictionary<(Guid SalonId, DateOnly Date), ResolvedWindow>();

        foreach (var window in windows)
        {
            var zone = Resolve(window.TimeZoneId, zones, unresolvedZones, log);
            if (zone is null)
            {
                continue;
            }

            // Повторное окно того же дня невозможно, но TryAdd дешевле проверки: первое
            // описание дня считается каноническим.
            windowsByKey.TryAdd((window.SalonId, window.Date), new ResolvedWindow(window, zone));
        }

        var conflicts = new List<ScheduleConflictResponse>();
        var seen = new HashSet<Guid>();

        foreach (var booking in bookings)
        {
            if (seen.Contains(booking.BookingId))
            {
                continue;
            }

            var bookingZone = Resolve(booking.TimeZoneId, zones, unresolvedZones, log);
            if (bookingZone is null)
            {
                continue;
            }

            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(booking.StartAt, bookingZone).DateTime);

            if (!windowsByKey.TryGetValue((booking.SalonId, localDate), out var resolved))
            {
                // Бронь другого салона или другого локального дня: эта правка её не касается.
                continue;
            }

            if (!IsOutside(resolved, booking, log))
            {
                continue;
            }

            seen.Add(booking.BookingId);
            conflicts.Add(ToResponse(booking));
        }

        return [.. conflicts
            .OrderBy(conflict => conflict.StartAt)
            .ThenBy(conflict => conflict.Id)];
    }

    /// <summary>
    /// Записи, пересекающиеся с интервалом отпуска.
    /// </summary>
    /// <remarks>
    /// Отпуск хранится абсолютным интервалом, поэтому пояса здесь не нужны: пересечение —
    /// это <c>StartAt &lt; endAt &amp;&amp; startAt &lt; EndAt</c>. Отпуск, заканчивающийся
    /// ровно в начале визита, конфликтом не считается.
    /// </remarks>
    /// <param name="startAt">Начало отпуска.</param>
    /// <param name="endAt">Окончание отпуска.</param>
    /// <param name="bookings">Активные брони барбера.</param>
    /// <returns>Затронутые записи, по <c>StartAt</c> возрастанию, затем по идентификатору.</returns>
    public static IReadOnlyList<ScheduleConflictResponse> FindInsideTimeOff(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        IReadOnlyList<ScheduleConflictBooking> bookings)
    {
        ArgumentNullException.ThrowIfNull(bookings);

        if (endAt <= startAt || bookings.Count == 0)
        {
            return [];
        }

        var conflicts = new List<ScheduleConflictResponse>();
        var seen = new HashSet<Guid>();

        foreach (var booking in bookings
                     .Where(booking => booking.StartAt < endAt && startAt < booking.EndAt)
                     .OrderBy(booking => booking.StartAt)
                     .ThenBy(booking => booking.BookingId))
        {
            if (seen.Add(booking.BookingId))
            {
                conflicts.Add(ToResponse(booking));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Помещается ли запись в окно дня.
    /// </summary>
    private static bool IsOutside(
        ResolvedWindow resolved,
        ScheduleConflictBooking booking,
        ILogger log)
    {
        var window = resolved.Window;

        if (window.IsDayOff)
        {
            return true;
        }

        var dayStart = SalonTimeZone.ToUtc(window.Date.ToDateTime(window.Start), resolved.Zone);
        var dayEnd = SalonTimeZone.ToUtc(window.Date.ToDateTime(window.End), resolved.Zone);

        if (dayStart is null || dayEnd is null)
        {
            log.LogWarning(
                "Schedule conflict check skipped {Date} in time zone {TimeZoneId}: "
                + "the local boundary does not exist because of a daylight saving transition.",
                window.Date,
                window.TimeZoneId);

            return false;
        }

        // Касание границ — не конфликт: визит ровно до закрытия нормален.
        return booking.StartAt < dayStart.Value || booking.EndAt > dayEnd.Value;
    }

    /// <summary>
    /// Разрешает пояс и запоминает результат на время вызова.
    /// </summary>
    /// <remarks>
    /// Неразрешимый пояс — ошибка данных, а не сбой запроса: день не проверяется, в журнал
    /// пишется одна запись на пояс. На таком профиле слоты и так отвечают
    /// <c>409 barberSlots.timeZone.*</c>.
    /// </remarks>
    private static TimeZoneInfo? Resolve(
        string? timeZoneId,
        Dictionary<string, TimeZoneInfo?> zones,
        HashSet<string> unresolvedZones,
        ILogger log)
    {
        var key = timeZoneId ?? string.Empty;

        if (zones.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (SalonTimeZone.TryResolve(timeZoneId, out var zone))
        {
            zones[key] = zone;
            return zone;
        }

        zones[key] = null;

        if (unresolvedZones.Add(key))
        {
            log.LogError(
                "Schedule conflict check skipped: time zone '{TimeZoneId}' could not be resolved.",
                string.IsNullOrWhiteSpace(key) ? "(missing)" : key);
        }

        return null;
    }

    private static ScheduleConflictResponse ToResponse(ScheduleConflictBooking booking)
        => new(
            booking.BookingId,
            booking.StartAt,
            booking.EndAt,
            booking.TimeZoneId,
            booking.CustomerName,
            booking.ServiceName,
            booking.Status);

    /// <summary>
    /// Окно вместе с уже разрешённым поясом — чтобы не разрешать его второй раз.
    /// </summary>
    private sealed record ResolvedWindow(ScheduleDayWindow Window, TimeZoneInfo Zone);
}
