using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Один день недели в редакторе расписания: рабочий или выходной и его часы.
/// </summary>
/// <remarks>
/// <para>
/// Часы хранятся как <see cref="TimeSpan"/>, а не <see cref="TimeOnly"/>: <c>TimePicker.Time</c>
/// в MAUI привязан именно к <see cref="TimeSpan"/>, и конвертер в разметке не нужен.
/// </para>
/// <para>
/// У выходного дня часы остаются осмысленными 10:00–20:00, а не <c>00:00</c>. Сервер хранит
/// выходной с <c>TimeOnly.MinValue</c>, и если показать его как есть, включение дня обратно
/// началось бы с невалидного «00:00–00:00»; веб делает так же
/// (<c>weeklyDaysToInput</c> в <c>barber-schedule.ts</c>).
/// </para>
/// <para>
/// <see cref="IsWorking"/> — то, что видит человек (переключатель), <see cref="IsDayOff"/> —
/// то, что уйдёт на сервер. Это одна и та же величина, поэтому сеттер одного поднимает
/// изменение другого.
/// </para>
/// </remarks>
public sealed class BarberScheduleDayItem : ObservableObject
{
    /// <summary>
    /// Начало рабочего дня по умолчанию: 10:00.
    /// </summary>
    public static readonly TimeSpan DefaultStartTime = TimeSpan.FromHours(10);

    /// <summary>
    /// Окончание рабочего дня по умолчанию: 20:00.
    /// </summary>
    public static readonly TimeSpan DefaultEndTime = TimeSpan.FromHours(20);

    private bool _isDayOff;
    private TimeSpan _startTime;
    private TimeSpan _endTime;

    /// <summary>
    /// Создаёт день недели для редактора.
    /// </summary>
    /// <param name="dayOfWeek">День недели.</param>
    /// <param name="isDayOff">Выходной ли это день.</param>
    /// <param name="startTime">Начало работы; для выходного хранится как есть.</param>
    /// <param name="endTime">Окончание работы; для выходного хранится как есть.</param>
    public BarberScheduleDayItem(
        DayOfWeek dayOfWeek,
        bool isDayOff,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        DayOfWeek = dayOfWeek;
        _isDayOff = isDayOff;
        _startTime = Clamp(startTime);
        _endTime = Clamp(endTime);
    }

    /// <summary>
    /// День недели из ответа API.
    /// </summary>
    public DayOfWeek DayOfWeek { get; }

    /// <summary>
    /// Подпись дня: «Понедельник»… «Воскресенье».
    /// </summary>
    public string DayLabel => BarberScheduleText.Weekday(DayOfWeek);

    /// <summary>
    /// Выходной ли это день; именно это значение уходит на сервер.
    /// </summary>
    public bool IsDayOff
    {
        get => _isDayOff;
        set
        {
            if (!SetProperty(ref _isDayOff, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(ShowDayOffText));
            OnPropertyChanged(nameof(DayOffText));
            OnPropertyChanged(nameof(IsInvalid));
            OnPropertyChanged(nameof(HoursText));
        }
    }

    /// <summary>
    /// Работает ли мастер в этот день: переключатель в разметке.
    /// </summary>
    public bool IsWorking
    {
        get => !_isDayOff;
        set => IsDayOff = !value;
    }

    /// <summary>
    /// Начало рабочего дня для <c>TimePicker.Time</c>.
    /// </summary>
    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, Clamp(value)))
            {
                OnPropertyChanged(nameof(IsInvalid));
                OnPropertyChanged(nameof(HoursText));
            }
        }
    }

    /// <summary>
    /// Окончание рабочего дня для <c>TimePicker.Time</c>.
    /// </summary>
    public TimeSpan EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, Clamp(value)))
            {
                OnPropertyChanged(nameof(IsInvalid));
                OnPropertyChanged(nameof(HoursText));
            }
        }
    }

    /// <summary>
    /// Часы противоречат друг другу: рабочий день кончается не позже, чем начинается.
    /// </summary>
    /// <remarks>
    /// Проверка локальная: сервер отклонил бы такую неделю, но человек должен увидеть причину
    /// до нажатия «Сохранить», а не после ответа.
    /// </remarks>
    public bool IsInvalid => !_isDayOff && _endTime <= _startTime;

    /// <summary>
    /// Показывать ли подпись «Выходной» вместо полей времени.
    /// </summary>
    public bool ShowDayOffText => _isDayOff;

    /// <summary>
    /// Подпись выходного дня.
    /// </summary>
    public string DayOffText => _isDayOff ? Loc.T("Выходной") : string.Empty;

    /// <summary>
    /// Часы дня строкой: «10:00–20:00»; для выходного — пусто.
    /// </summary>
    public string HoursText => _isDayOff
        ? string.Empty
        : BarberScheduleText.TimeRange(ToTimeOnly(_startTime), ToTimeOnly(_endTime));

    /// <summary>
    /// Собирает день из ответа сервера; отсутствующий день считается выходным.
    /// </summary>
    /// <param name="dayOfWeek">День недели, который нужен редактору.</param>
    /// <param name="response">День из ответа API; <c>null</c> — день не пришёл.</param>
    /// <returns>Строка редактора.</returns>
    public static BarberScheduleDayItem From(
        DayOfWeek dayOfWeek,
        BarberScheduleDayResponse? response)
    {
        if (response is null || response.IsDayOff)
        {
            return new BarberScheduleDayItem(dayOfWeek, true, DefaultStartTime, DefaultEndTime);
        }

        return new BarberScheduleDayItem(
            dayOfWeek,
            false,
            TimeSpan.FromTicks(response.StartTime.Ticks),
            TimeSpan.FromTicks(response.EndTime.Ticks));
    }

    /// <summary>
    /// Переводит часы в <see cref="TimeOnly"/>: значение уже в пределах суток.
    /// </summary>
    /// <param name="value">Часы дня.</param>
    /// <returns>Время суток.</returns>
    internal static TimeOnly ToTimeOnly(TimeSpan value) =>
        new(value.Hours, value.Minutes);

    /// <summary>
    /// Приводит время из <c>TimePicker</c> к суткам без секунд.
    /// </summary>
    /// <remarks>
    /// <c>TimePicker.Time</c> на разных платформах может прийти с секундами или с лишним днём
    /// (например, <c>1.00:00:00</c> вместо полуночи). Секунды отбрасываются, а значение
    /// зажимается в 00:00–23:59, чтобы <see cref="TimeOnly"/> строился без исключения.
    /// </remarks>
    /// <param name="value">Время из привязки.</param>
    /// <returns>Время в пределах суток.</returns>
    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value >= TimeSpan.FromDays(1)
            ? new TimeSpan(23, 59, 0)
            : new TimeSpan(value.Hours, value.Minutes, 0);
    }
}
