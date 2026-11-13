using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Application.Common;

/// <summary>
/// Сетка слотов барбера на локальную дату салона — чистая функция без базы и без «сейчас».
/// </summary>
/// <remarks>
/// <para>
/// Границы рабочего интервала считаются в часовом поясе салона (<c>Salon.TimeZoneId</c>)
/// и только затем переводятся в UTC — неявного UTC-сдвига нет. Сетка строится по локальному
/// времени салона, поэтому переход на летнее время не сдвигает начало рабочего дня.
/// Дыра перехода на летнее время пропускается (несуществующее локальное время нельзя
/// перевести в UTC), а повтор часа дедуплицируется по UTC-инстанту.
/// </para>
/// <para>
/// Прошедшие слоты и занятость не фильтруются: у публичных слотов и у чтения слотов переноса
/// разные источники «сейчас» и разные наборы броней, поэтому фильтр — дело вызывающего.
/// Так же и отпуска: они передаются сюда, потому что принадлежат профилю, а не салону.
/// </para>
/// </remarks>
public static class SlotGrid
{
    /// <summary>
    /// Легальные старты сетки на локальную дату: границы дня салона, шаг = <paramref name="durationMinutes"/>,
    /// старт + длительность не выходит за конец дня, слот не пересекается с отпуском.
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
    /// <param name="durationMinutes">Шаг сетки и длительность слота в минутах.</param>
    /// <param name="timeOffs">Отпуска барбера, пересекающиеся с окном вокруг даты.</param>
    public static IReadOnlyList<BarberSlotResponse> Legal(
        DateOnly date,
        string timeZoneId,
        TimeOnly startTime,
        TimeOnly endTime,
        int durationMinutes,
        IReadOnlyList<TimeOffInterval> timeOffs)
    {
        ArgumentNullException.ThrowIfNull(timeOffs);

        if (durationMinutes <= 0)
        {
            return [];
        }

        if (!SalonTimeZone.TryResolve(timeZoneId, out var timeZone))
        {
            return [];
        }

        var workStartLocal = date.ToDateTime(startTime);
        var workEndLocal = date.ToDateTime(endTime);
        var slots = new List<BarberSlotResponse>();
        var seen = new HashSet<DateTimeOffset>();

        for (var localStart = workStartLocal;
             localStart.AddMinutes(durationMinutes) <= workEndLocal;
             localStart = localStart.AddMinutes(durationMinutes))
        {
            var localEnd = localStart.AddMinutes(durationMinutes);
            var slotStart = SalonTimeZone.ToUtc(localStart, timeZone);
            var slotEnd = SalonTimeZone.ToUtc(localEnd, timeZone);

            if (slotStart is null || slotEnd is null)
            {
                // Несуществующее локальное время (переход на летнее время) пропускается.
                continue;
            }

            // При переходе на зимнее время два локальных времени могут дать один UTC-инстант.
            if (!seen.Add(slotStart.Value))
            {
                continue;
            }

            if (timeOffs.Any(timeOff =>
                    timeOff.StartAt < slotEnd.Value && slotStart.Value < timeOff.EndAt))
            {
                continue;
            }

            slots.Add(new BarberSlotResponse(slotStart.Value, slotEnd.Value));
        }

        return slots;
    }
}
