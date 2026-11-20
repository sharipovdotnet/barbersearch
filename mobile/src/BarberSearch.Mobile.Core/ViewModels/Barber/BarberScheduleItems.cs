using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Салон в выборе раздела «Расписание».
/// </summary>
/// <remarks>
/// <para>
/// Строка неизменяема: салоны приходят ответом, а не правятся на клиенте. Недельный шаблон
/// и исключения лежат в ответе и пересобираются при смене салона — строка выбора их не носит,
/// иначе два источника одной недели разошлись бы после сохранения.
/// </para>
/// <para>
/// <see cref="TimeZone"/> разрешается один раз в конструкторе: у каждого салона свой пояс, и
/// повторный <c>FindSystemTimeZoneById</c> на каждую строку списка — лишняя работа.
/// Неизвестный пояс остаётся <c>null</c>, и время показывается в UTC.
/// </para>
/// </remarks>
public sealed class BarberScheduleSalonItem
{
    /// <summary>
    /// Создаёт строку выбора салона.
    /// </summary>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="name">Название салона.</param>
    /// <param name="city">Город салона.</param>
    /// <param name="timeZoneId">IANA-пояс салона.</param>
    /// <param name="isPrimary">Основной ли это салон профиля.</param>
    public BarberScheduleSalonItem(
        Guid salonId,
        string name,
        string city,
        string timeZoneId,
        bool isPrimary)
    {
        SalonId = salonId;
        Name = name;
        City = city;
        TimeZoneId = timeZoneId;
        IsPrimary = isPrimary;
        TimeZone = SalonClock.TryResolve(timeZoneId);
    }

    /// <summary>
    /// Создаёт строку выбора из ответа сервера.
    /// </summary>
    /// <param name="salon">Салон из ответа <c>GET api/barber-profiles/{id}/schedule</c>.</param>
    public BarberScheduleSalonItem(BarberScheduleSalonResponse salon)
        : this(salon.SalonId, salon.Name, salon.City, salon.TimeZoneId, salon.IsPrimary)
    {
    }

    /// <summary>
    /// Идентификатор салона.
    /// </summary>
    public Guid SalonId { get; }

    /// <summary>
    /// Название салона.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Город салона.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// Часовой пояс салона в формате IANA.
    /// </summary>
    public string TimeZoneId { get; }

    /// <summary>
    /// Основной салон профиля.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Разобранный пояс салона; <c>null</c>, если идентификатор неизвестен.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; }

    /// <summary>
    /// Подпись строки: «Название · Город»; город не повторяется, если он уже в названии.
    /// </summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(City) || Name.Contains(City, StringComparison.OrdinalIgnoreCase)
            ? Name
            : string.Concat(Name, " · ", City);
}

/// <summary>
/// Исключение на дату в списке исключений.
/// </summary>
/// <remarks>
/// Дата исключения — локальная дата салона, поэтому она печатается как есть, без пересчёта в
/// пояс телефона. Выходной не показывает часы: их у него нет.
/// </remarks>
public sealed class BarberScheduleExceptionItem
{
    /// <summary>
    /// Создаёт строку исключения из ответа.
    /// </summary>
    /// <param name="exception">Исключение из ответа API.</param>
    public BarberScheduleExceptionItem(BarberScheduleExceptionResponse exception)
    {
        Date = exception.Date;
        IsDayOff = exception.IsDayOff;
        StartTime = exception.StartTime;
        EndTime = exception.EndTime;
        Reason = exception.Reason;
    }

    /// <summary>
    /// Локальная дата салона.
    /// </summary>
    public DateOnly Date { get; }

    /// <summary>
    /// Выходной ли это день.
    /// </summary>
    public bool IsDayOff { get; }

    /// <summary>
    /// Начало своих часов; для выходного — <c>00:00</c>.
    /// </summary>
    public TimeOnly StartTime { get; }

    /// <summary>
    /// Окончание своих часов; для выходного — <c>00:00</c>.
    /// </summary>
    public TimeOnly EndTime { get; }

    /// <summary>
    /// Пометка мастера; <c>null</c>, если её нет.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Дата строкой: «15 ноября 2026».
    /// </summary>
    public string DateText => BarberScheduleText.Date(Date);

    /// <summary>
    /// Часы строкой: «Выходной» или «10:00–14:00».
    /// </summary>
    public string HoursText => IsDayOff
        ? Loc.T("Выходной")
        : BarberScheduleText.TimeRange(StartTime, EndTime);

    /// <summary>
    /// Пометка строкой; <c>null</c>, если пометки нет.
    /// </summary>
    public string? ReasonText =>
        string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    /// <summary>
    /// Есть ли пометка.
    /// </summary>
    public bool HasReason => ReasonText is not null;

    /// <summary>
    /// Вторая строка строки: часы и, если есть, пометка.
    /// </summary>
    public string SummaryText => ReasonText is null
        ? HoursText
        : string.Concat(HoursText, " · ", ReasonText);

    /// <summary>
    /// Подпись кнопки удаления для экранного диктора: «Удалить исключение 15 ноября 2026».
    /// </summary>
    public string RemoveLabel => Loc.T("Удалить исключение {0}", DateText);
}

/// <summary>
/// Отпуск или другой нерабочий период в списке.
/// </summary>
/// <remarks>
/// Период принадлежит профилю, а не салону, поэтому показывается в поясе выбранного салона:
/// «С 15 по 17 ноября» в Москве и в другом городе — разные UTC-границы одного отпуска.
/// </remarks>
public sealed class BarberTimeOffItem
{
    private readonly TimeZoneInfo? _timeZone;

    /// <summary>
    /// Создаёт строку отпуска.
    /// </summary>
    /// <param name="timeOff">Период из ответа API.</param>
    /// <param name="timeZone">Пояс салона для показа; <c>null</c> — UTC.</param>
    public BarberTimeOffItem(BarberScheduleTimeOffResponse timeOff, TimeZoneInfo? timeZone)
    {
        Id = timeOff.Id;
        StartAt = timeOff.StartAt;
        EndAt = timeOff.EndAt;
        Reason = timeOff.Reason;
        _timeZone = timeZone;
    }

    /// <summary>
    /// Идентификатор периода.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Начало периода в UTC.
    /// </summary>
    public DateTimeOffset StartAt { get; }

    /// <summary>
    /// Окончание периода в UTC; граница исключающая.
    /// </summary>
    public DateTimeOffset EndAt { get; }

    /// <summary>
    /// Пометка мастера; <c>null</c>, если её нет.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Период строкой: «15–17 ноября 2026».
    /// </summary>
    public string RangeText => BarberScheduleText.TimeOffRange(StartAt, EndAt, _timeZone);

    /// <summary>
    /// Пометка строкой; <c>null</c>, если пометки нет.
    /// </summary>
    public string? ReasonText =>
        string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    /// <summary>
    /// Есть ли пометка.
    /// </summary>
    public bool HasReason => ReasonText is not null;

    /// <summary>
    /// Подпись кнопки удаления для экранного диктора: «Удалить отпуск 15–17 ноября 2026».
    /// </summary>
    public string RemoveLabel => Loc.T("Удалить отпуск {0}", RangeText);
}

/// <summary>
/// Затронутый визит в диалоге конфликта расписания.
/// </summary>
/// <remarks>
/// Показывается в поясе салона записи, а не салона редактора: визит мог состояться в другом
/// филиале, и время должно совпадать с тем, что видит клиент. Телефона и цены здесь нет —
/// диалог объясняет, а не служит рабочей карточкой.
/// </remarks>
public sealed class ScheduleConflictItem
{
    /// <summary>
    /// Создаёт строку конфликта из ответа.
    /// </summary>
    /// <param name="conflict">Визит из расширения <c>conflicts</c>.</param>
    public ScheduleConflictItem(ScheduleConflictResponse conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        Id = conflict.Id;
        Customer = conflict.CustomerName;
        Service = conflict.ServiceName;
        Status = BarberScheduleText.Status(conflict.Status);
        When = BarberScheduleText.ConflictWhen(
            conflict.StartAt,
            SalonClock.TryResolve(conflict.TimeZoneId));
    }

    /// <summary>
    /// Идентификатор брони.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Момент визита: «сб, 15 ноября в 10:00».
    /// </summary>
    public string When { get; }

    /// <summary>
    /// Имя клиента.
    /// </summary>
    public string Customer { get; }

    /// <summary>
    /// Название услуги.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// Статус записи словом.
    /// </summary>
    public string Status { get; }
}
