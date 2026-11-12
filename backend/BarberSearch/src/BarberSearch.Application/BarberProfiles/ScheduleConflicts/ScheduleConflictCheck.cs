using BarberSearch.Application.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Общий шаг пишущих ручек расписания: прочитать активные брони и найти затронутые.
/// </summary>
/// <remarks>
/// Вызывается только внутри транзакции и уже после <c>EnsureCanManage</c>. Начавшиеся визиты
/// отбрасываются до проверки: правка расписания не должна «конфликтовать» с визитом, который
/// идёт прямо сейчас (D5). При <c>force</c> этот шаг не вызывается вовсе — брони не читаются.
/// </remarks>
internal static class ScheduleConflictCheck
{
    /// <summary>
    /// Конфликты «запись не помещается в новые часы своего локального дня».
    /// </summary>
    /// <param name="bookings">Репозиторий броней.</param>
    /// <param name="barberProfileId">Барбер, чьё расписание меняется.</param>
    /// <param name="windows">Окна изменённых дней.</param>
    /// <param name="now">Текущий момент UTC — нижняя граница проверки.</param>
    /// <param name="to">Конец интервала чтения (заведомо не раньше последнего окна).</param>
    /// <param name="logger">Журнал обработчика.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task<Result<ScheduleWriteOutcome, Failure>> FindOutsideHoursAsync(
        IBookingRepository bookings,
        Guid barberProfileId,
        IReadOnlyList<ScheduleDayWindow> windows,
        DateTimeOffset now,
        DateTimeOffset to,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(logger);

        if (windows.Count == 0)
        {
            return ScheduleWriteOutcome.Saved;
        }

        var rows = await bookings.GetActiveForScheduleAsync(
            barberProfileId,
            now,
            to,
            cancellationToken);
        if (rows.IsFailure)
        {
            return rows.Error;
        }

        var future = rows.Value
            .Where(booking => booking.StartAt > now)
            .ToList();

        var conflicts = ScheduleConflictDetector.FindOutsideHours(windows, future, logger);

        return conflicts.Count == 0
            ? ScheduleWriteOutcome.Saved
            : ScheduleWriteOutcome.Blocked(conflicts);
    }

    /// <summary>
    /// Конфликты «запись пересекается с отпуском».
    /// </summary>
    /// <param name="bookings">Репозиторий броней.</param>
    /// <param name="barberProfileId">Барбер, которому добавляют отпуск.</param>
    /// <param name="startAt">Начало отпуска.</param>
    /// <param name="endAt">Окончание отпуска.</param>
    /// <param name="now">Текущий момент UTC — нижняя граница проверки.</param>
    /// <param name="logger">Журнал обработчика.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task<Result<ScheduleWriteOutcome, Failure>> FindInsideTimeOffAsync(
        IBookingRepository bookings,
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(logger);

        // Некорректный интервал отклонит домен; будущих визитов у отпуска в прошлом нет.
        if (endAt <= startAt || endAt <= now)
        {
            return ScheduleWriteOutcome.Saved;
        }

        var rows = await bookings.GetActiveForScheduleAsync(
            barberProfileId,
            now,
            endAt,
            cancellationToken);
        if (rows.IsFailure)
        {
            return rows.Error;
        }

        var future = rows.Value
            .Where(booking => booking.StartAt > now)
            .ToList();

        var conflicts = ScheduleConflictDetector.FindInsideTimeOff(startAt, endAt, future);

        return conflicts.Count == 0
            ? ScheduleWriteOutcome.Saved
            : ScheduleWriteOutcome.Blocked(conflicts);
    }
}
