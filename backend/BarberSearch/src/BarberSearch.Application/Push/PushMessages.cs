using System.Globalization;
using System.Text;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Push;

namespace BarberSearch.Application.Push;

/// <summary>
/// Русские тексты push-уведомлений клиенту: напоминание и события создания брони.
/// </summary>
/// <remarks>
/// <para>
/// Тексты — часть наблюдаемого поведения и приходят байт в байт: напоминание —
/// <c>Через час: салон «Барбершоп»</c> / <c>Мастер Иван · Стрижка · сегодня в 14:30</c>,
/// события создания — <c>Вы записались: салон «Барбершоп»</c> или
/// <c>Вас записали: салон «Барбершоп»</c>. Поэтому они живут в одном месте, а не собираются
/// по месту отправки, и проверяются тестами на точное совпадение.
/// </para>
/// <para>
/// Слово «сегодня»/«завтра» считается по локальной дате <b>салона</b>, а не сервера:
/// напоминание за час до полуночи пересекает сутки, и UTC-дата дала бы неверное слово.
/// Если до визита больше суток (событие создания приходит сразу, а запись — на любой день
/// горизонта 30 дней), слово заменяется датой в родительном падеже: <c>24 ноября</c>.
/// Неизвестный пояс не роняет отправку — время показывается в UTC, как и в напоминании
/// барберу.
/// </para>
/// <para>
/// Отсутствующий снимок просто выпадает из перечисления: заголовок без салона деградирует
/// до «к мастеру», тело без мастера или услуги остаётся читаемым и не содержит «null».
/// Персональных данных клиента здесь нет и быть не может: методы получают только снимок
/// визита (152-ФЗ, решение D3).
/// </para>
/// </remarks>
public static class PushMessages
{
    /// <summary>
    /// Заголовок напоминания, когда салон неизвестен.
    /// </summary>
    public const string DEFAULT_TITLE = "Через час — визит к мастеру";

    /// <summary>
    /// Заголовок события «клиент записался сам», когда салон неизвестен.
    /// </summary>
    public const string CUSTOMER_CREATED_DEFAULT_TITLE = "Вы записались к мастеру";

    /// <summary>
    /// Заголовок события «барбер записал клиента», когда салон неизвестен.
    /// </summary>
    public const string BARBER_CREATED_DEFAULT_TITLE = "Вас записали к мастеру";

    /// <summary>
    /// Слово для визита в те же локальные сутки салона.
    /// </summary>
    public const string TODAY = "сегодня";

    /// <summary>
    /// Слово для визита в следующие локальные сутки салона.
    /// </summary>
    public const string TOMORROW = "завтра";

    /// <summary>
    /// Разделитель частей тела сообщения.
    /// </summary>
    public const string PART_SEPARATOR = " · ";

    /// <summary>
    /// Названия месяцев в родительном падеже: <c>24 ноября</c>, без года — горизонт записи
    /// 30 дней, и год в тексте был бы шумом.
    /// </summary>
    private static readonly string[] MonthNames =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    /// <summary>
    /// Собирает push-сообщение о визите.
    /// </summary>
    /// <remarks>
    /// <paramref name="now"/> передаёт Application: домен бизнес-«сейчас» не читает
    /// (AGENT.md §7.6), а слово «сегодня» — бизнес-решение, а не техническая метка.
    /// </remarks>
    /// <param name="notification">Снимок визита.</param>
    /// <param name="now">Текущий момент в UTC.</param>
    public static PushMessage Reminder(PushNotification notification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var timeZone = ResolveTimeZone(notification.SalonTimeZoneId);

        return new PushMessage(
            ComposeTitle(notification.SalonName),
            ComposeBody(notification, now, timeZone),
            notification.BookingId,
            PushMessage.REMINDER_TYPE);
    }

    /// <summary>
    /// Собирает сообщение по виду уведомления: напоминание или событие создания брони.
    /// </summary>
    /// <param name="notification">Снимок визита с видом уведомления.</param>
    /// <param name="now">Текущий момент в UTC.</param>
    public static PushMessage For(PushNotification notification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.Kind switch
        {
            PushNotificationKind.Reminder => Reminder(notification, now),
            PushNotificationKind.BookingCreatedByCustomer =>
                Created(notification, now, createdByBarber: false),
            PushNotificationKind.BookingCreatedByBarber =>
                Created(notification, now, createdByBarber: true),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                notification.Kind,
                "Unsupported push notification kind.")
        };
    }

    /// <summary>
    /// Собирает заголовок напоминания: с салоном — «Через час: салон «…»», без него — деградированный.
    /// </summary>
    /// <param name="salonName">Снимок салона или <c>null</c>.</param>
    public static string ComposeTitle(string? salonName)
        => string.IsNullOrWhiteSpace(salonName)
            ? DEFAULT_TITLE
            : "Через час: салон «" + salonName.Trim() + "»";

    /// <summary>
    /// Собирает заголовок события создания брони.
    /// </summary>
    /// <param name="salonName">Снимок салона или <c>null</c>.</param>
    /// <param name="createdByBarber">Бронь внёс барбер, а не сам клиент.</param>
    public static string ComposeCreatedTitle(string? salonName, bool createdByBarber)
    {
        if (string.IsNullOrWhiteSpace(salonName))
        {
            return createdByBarber
                ? BARBER_CREATED_DEFAULT_TITLE
                : CUSTOMER_CREATED_DEFAULT_TITLE;
        }

        var prefix = createdByBarber ? "Вас записали" : "Вы записались";

        return prefix + ": салон «" + salonName.Trim() + "»";
    }

    /// <summary>
    /// Собирает событие создания брони: текст зависит только от того, кто создал запись.
    /// </summary>
    private static PushMessage Created(
        PushNotification notification,
        DateTimeOffset now,
        bool createdByBarber)
    {
        var timeZone = ResolveTimeZone(notification.SalonTimeZoneId);

        return new PushMessage(
            ComposeCreatedTitle(notification.SalonName, createdByBarber),
            ComposeCreatedBody(notification, now, timeZone),
            notification.BookingId,
            PushMessage.BOOKING_CREATED_TYPE);
    }

    /// <summary>
    /// Собирает тело напоминания: мастер, услуга и день со временем; отсутствующие части выпадают.
    /// </summary>
    private static string ComposeBody(
        PushNotification notification,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(notification.BarberName))
        {
            builder.Append("Мастер ").Append(notification.BarberName.Trim()).Append(PART_SEPARATOR);
        }

        if (!string.IsNullOrWhiteSpace(notification.ServiceName))
        {
            builder.Append(notification.ServiceName.Trim()).Append(PART_SEPARATOR);
        }

        builder
            .Append(DayWord(notification.StartAt, now, timeZone))
            .Append(" в ")
            .Append(SalonTimeZone.FormatTimeOfDay(notification.StartAt, timeZone));

        return builder.ToString();
    }

    /// <summary>
    /// Собирает тело события создания: мастер, услуга и день со временем; отсутствующие части
    /// выпадают точно так же, как у напоминания.
    /// </summary>
    private static string ComposeCreatedBody(
        PushNotification notification,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(notification.BarberName))
        {
            builder.Append("Мастер ").Append(notification.BarberName.Trim()).Append(PART_SEPARATOR);
        }

        if (!string.IsNullOrWhiteSpace(notification.ServiceName))
        {
            builder.Append(notification.ServiceName.Trim()).Append(PART_SEPARATOR);
        }

        builder
            .Append(CreationDayWord(notification.StartAt, now, timeZone))
            .Append(" в ")
            .Append(SalonTimeZone.FormatTimeOfDay(notification.StartAt, timeZone));

        return builder.ToString();
    }

    /// <summary>
    /// Разрешает пояс салона; неизвестный пояс даёт UTC, а не отказ от отправки.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? salonTimeZoneId)
        => SalonTimeZone.TryResolve(salonTimeZoneId, out var resolved)
            ? resolved
            : TimeZoneInfo.Utc;

    /// <summary>
    /// Выбирает «сегодня» или «завтра» по локальным датам салона.
    /// </summary>
    /// <remarks>
    /// Напоминание создаётся за <c>Push:ReminderLeadMinutes</c> (по умолчанию 60 минут) до
    /// начала, поэтому пересечь можно не больше одних суток: любая дата, отличная от
    /// сегодняшней локальной, — завтрашняя. Отдельная ветка на «послезавтра» не нужна и
    /// скрыла бы ошибку в данных, поэтому её нет.
    /// </remarks>
    private static string DayWord(
        DateTimeOffset startAt,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime);
        var nowDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

        return startDate == nowDate ? TODAY : TOMORROW;
    }

    /// <summary>
    /// Выбирает день события создания: сегодня, завтра или дату в родительном падеже.
    /// </summary>
    /// <remarks>
    /// Событие приходит сразу после записи, а сама запись может быть на любой день горизонта
    /// (30 дней), поэтому в отличие от напоминания «не сегодня — значит завтра» здесь неверно.
    /// Год не печатается: горизонт не пересекает его больше чем на месяц.
    /// </remarks>
    private static string CreationDayWord(
        DateTimeOffset startAt,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime);
        var nowDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

        if (startDate == nowDate)
        {
            return TODAY;
        }

        if (startDate == nowDate.AddDays(1))
        {
            return TOMORROW;
        }

        return startDate.Day.ToString(CultureInfo.InvariantCulture)
               + " "
               + MonthNames[startDate.Month - 1];
    }
}
