using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Application.Common;

/// <summary>
/// Свободные старты визита на локальную дату салона — чистая функция без базы и без «сейчас».
/// </summary>
/// <remarks>
/// <para>
/// Сетка строится не «от открытия дня с фиксированным шагом», а по свободным окнам: рабочий
/// день минус отпуска минус активные брони. В каждом окне слоты идут от его начала — то есть
/// первый из них примыкает к концу предыдущего визита, — а последний примыкает к следующему
/// визиту. Поэтому клиенту предлагается время, которое заполняет день без дыры перед собой,
/// а не только «круглые» часы от открытия: после брони 12:00–12:30 часовая услуга получает
/// 12:30, хотя при фиксированной сетке от 10:00 её там не было бы.
/// </para>
/// <para>
/// Границы рабочего интервала считаются в часовом поясе салона (<c>Salon.TimeZoneId</c>)
/// и только затем переводятся в UTC — неявного UTC-сдвига нет. Дыра перехода на летнее
/// время пропускается (несуществующее локальное время нельзя перевести в UTC), а повтор
/// часа дедуплицируется по UTC-инстанту.
/// </para>
/// <para>
/// Прошедшие слоты здесь не фильтруются: у публичных слотов, у слотов переноса и у ручной
/// записи барбера разные источники «сейчас», поэтому фильтр — дело вызывающего. Отпуска
/// и занятость, наоборот, входят сюда: без них «свободный» слот мог бы пересечься
/// с чужим визитом.
/// </para>
/// </remarks>
public static class SlotGrid
{
    /// <summary>
    /// Свободные старты сетки на локальную дату: визит целиком помещается в свободное окно
    /// дня, не пересекается с отпуском и активной бронью, а начало идёт с шагом длительности
    /// или примыкает к границе окна.
    /// </summary>
    /// <remarks>
    /// Неразрешимый пояс и неположительная длительность дают пустую сетку: это исторические
    /// или повреждённые данные, а решение «какой код вернуть» принимает вызывающий обработчик
    /// (у публичных слотов и у переноса он разный).
    /// </remarks>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="timeZoneId">IANA-идентификатор пояса салона.</param>
    /// <param name="startTime">Начало рабочего дня по локальному времени салона.</param>
    /// <param name="endTime">Окончание рабочего дня по локальному времени салона.</param>
    /// <param name="durationMinutes">Шаг сетки и длительность визита в минутах.</param>
    /// <param name="timeOffs">Отпуска барбера, пересекающиеся с окном вокруг даты.</param>
    /// <param name="busy">Активные брони барбера на эту дату: их время уже занято.</param>
    public static IReadOnlyList<BarberSlotResponse> Legal(
        DateOnly date,
        string timeZoneId,
        TimeOnly startTime,
        TimeOnly endTime,
        int durationMinutes,
        IReadOnlyList<TimeOffInterval> timeOffs,
        IReadOnlyList<BusyInterval> busy)
    {
        ArgumentNullException.ThrowIfNull(timeOffs);
        ArgumentNullException.ThrowIfNull(busy);

        if (durationMinutes <= 0)
        {
            return [];
        }

        if (!SalonTimeZone.TryResolve(timeZoneId, out var timeZone))
        {
            return [];
        }

        var workStart = date.ToDateTime(startTime);
        var workEnd = date.ToDateTime(endTime);

        if (workEnd <= workStart)
        {
            return [];
        }

        var step = TimeSpan.FromMinutes(durationMinutes);
        var blocked = MergeBlocked(timeOffs, busy, timeZone, workStart, workEnd);
        var slots = new List<BarberSlotResponse>();
        var seen = new HashSet<DateTimeOffset>();
        var cursor = workStart;

        foreach (var (blockedStart, blockedEnd) in blocked)
        {
            AddSegment(cursor, blockedStart);
            cursor = blockedEnd > cursor ? blockedEnd : cursor;
        }

        AddSegment(cursor, workEnd);

        // Дедупликация по UTC при переходе на зимнее время может нарушить локальный порядок,
        // поэтому итог сортируется по абсолютному моменту, а не по порядку добавления.
        slots.Sort((left, right) => left.StartAt.CompareTo(right.StartAt));

        return slots;

        // Свободное окно [segmentStart, segmentEnd) в локальном времени салона.
        void AddSegment(DateTime segmentStart, DateTime segmentEnd)
        {
            if (segmentEnd <= segmentStart || segmentEnd - segmentStart < step)
            {
                return;
            }

            // Первый слот окна примыкает к его началу: это конец предыдущей брони,
            // конец отпуска или открытие дня — перед таким визитом дыры не будет.
            for (var localStart = segmentStart;
                 localStart + step <= segmentEnd;
                 localStart += step)
            {
                AddSlot(localStart);
            }

            // Последний слот примыкает к концу окна: следующий визит начнётся сразу после него.
            // Совпадение с шагом отсекает дедупликация, поэтому проверять кратность не нужно.
            AddSlot(segmentEnd - step);
        }

        void AddSlot(DateTime localStart)
        {
            var localEnd = localStart + step;
            var slotStart = SalonTimeZone.ToUtc(localStart, timeZone);
            var slotEnd = SalonTimeZone.ToUtc(localEnd, timeZone);

            if (slotStart is null || slotEnd is null)
            {
                // Несуществующее локальное время (переход на летнее время) пропускается.
                return;
            }

            if (!seen.Add(slotStart.Value))
            {
                return;
            }

            slots.Add(new BarberSlotResponse(slotStart.Value, slotEnd.Value));
        }
    }

    /// <summary>
    /// Проверяет, помещается ли визит, начинающийся в <paramref name="startAt"/>, в день барбера.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Нужен ручной записи: время, которое вводит барбер, не обязано быть стартом из сетки
    /// (клиент пришёл к 10:07), но визит обязан целиком лежать в рабочем дне и не пересекаться
    /// с отпуском или чужой бронью. Публичная запись так не проверяет: там время обязано
    /// совпасть со стартом из <see cref="Legal"/>, иначе клиент записался бы мимо сетки.
    /// </para>
    /// <para>
    /// Конец визита считается по локальному времени салона (<c>local start + duration</c>),
    /// а не прибавлением минут к UTC: в день перевода часов это даёт ту же длительность
    /// по часам салона.
    /// </para>
    /// </remarks>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="timeZoneId">IANA-идентификатор пояса салона.</param>
    /// <param name="startTime">Начало рабочего дня по локальному времени салона.</param>
    /// <param name="endTime">Окончание рабочего дня по локальному времени салона.</param>
    /// <param name="durationMinutes">Длительность визита в минутах.</param>
    /// <param name="timeOffs">Отпуска барбера, пересекающиеся с окном вокруг даты.</param>
    /// <param name="busy">Активные брони барбера на эту дату.</param>
    /// <param name="startAt">Проверяемое начало визита.</param>
    /// <param name="slot">Слот с концом, посчитанным в поясе салона; только при <see cref="SlotFit.Fits"/>.</param>
    public static SlotFit Fit(
        DateOnly date,
        string timeZoneId,
        TimeOnly startTime,
        TimeOnly endTime,
        int durationMinutes,
        IReadOnlyList<TimeOffInterval> timeOffs,
        IReadOnlyList<BusyInterval> busy,
        DateTimeOffset startAt,
        out BarberSlotResponse? slot)
    {
        ArgumentNullException.ThrowIfNull(timeOffs);
        ArgumentNullException.ThrowIfNull(busy);

        slot = null;

        if (durationMinutes <= 0 || !SalonTimeZone.TryResolve(timeZoneId, out var timeZone))
        {
            return SlotFit.OutsideSchedule;
        }

        var workStart = date.ToDateTime(startTime);
        var workEnd = date.ToDateTime(endTime);

        if (workEnd <= workStart)
        {
            return SlotFit.OutsideSchedule;
        }

        var localStart = TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime;
        var localEnd = localStart.AddMinutes(durationMinutes);

        if (localStart < workStart || localEnd > workEnd)
        {
            return SlotFit.OutsideSchedule;
        }

        var endAt = SalonTimeZone.ToUtc(localEnd, timeZone);

        if (endAt is null)
        {
            return SlotFit.OutsideSchedule;
        }

        var endInstant = endAt.Value;

        if (timeOffs.Any(timeOff => timeOff.StartAt < endInstant && startAt < timeOff.EndAt))
        {
            return SlotFit.OutsideSchedule;
        }

        if (busy.Any(interval => interval.StartAt < endInstant && startAt < interval.EndAt))
        {
            return SlotFit.Overlap;
        }

        slot = new BarberSlotResponse(startAt.ToUniversalTime(), endInstant);

        return SlotFit.Fits;
    }

    /// <summary>
    /// Переводит отпуска и занятость в локальное время салона, обрезает их рабочим днём
    /// и сливает пересекающиеся интервалы — из результата получаются свободные окна.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> MergeBlocked(
        IReadOnlyList<TimeOffInterval> timeOffs,
        IReadOnlyList<BusyInterval> busy,
        TimeZoneInfo timeZone,
        DateTime workStart,
        DateTime workEnd)
    {
        var clipped = new List<(DateTime Start, DateTime End)>(timeOffs.Count + busy.Count);

        foreach (var (startAt, endAt) in timeOffs
                     .Select(interval => (interval.StartAt, interval.EndAt))
                     .Concat(busy.Select(interval => (interval.StartAt, interval.EndAt))))
        {
            var localStart = TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime;
            var localEnd = TimeZoneInfo.ConvertTime(endAt, timeZone).DateTime;

            if (localEnd <= workStart || localStart >= workEnd)
            {
                continue;
            }

            var clippedStart = localStart < workStart ? workStart : localStart;
            var clippedEnd = localEnd > workEnd ? workEnd : localEnd;

            if (clippedEnd > clippedStart)
            {
                clipped.Add((clippedStart, clippedEnd));
            }
        }

        clipped.Sort((left, right) => left.Start.CompareTo(right.Start));

        var merged = new List<(DateTime Start, DateTime End)>(clipped.Count);

        foreach (var interval in clipped)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, interval.End > last.End ? interval.End : last.End);
                continue;
            }

            merged.Add(interval);
        }

        return merged;
    }
}
