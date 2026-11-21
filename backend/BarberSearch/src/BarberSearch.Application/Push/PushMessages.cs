using System.Globalization;
using System.Text;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Push;

namespace BarberSearch.Application.Push;

/// <summary>
/// Русские тексты push-уведомлений: клиенту — напоминание, события создания брони и события
/// её статуса; мастеру — новая запись клиента и её отмена.
/// </summary>
/// <remarks>
/// <para>
/// Тексты — часть наблюдаемого поведения и приходят байт в байт: напоминание —
/// <c>Через час: салон «Барбершоп»</c> / <c>Мастер Иван · Стрижка · сегодня в 14:30</c>,
/// события создания — <c>Вы записались: салон «Барбершоп»</c> или
/// <c>Вас записали: салон «Барбершоп»</c>, события статуса — <c>Ваша запись подтверждена:
/// салон «Барбершоп»</c>, <c>Ваша запись отменена: салон «Барбершоп»</c> и
/// <c>Ваша запись перенесена: салон «Барбершоп»</c>, а клиентские отмена и перенос с другой
/// поверхности — <c>Вы отменили запись: салон «Барбершоп»</c> и
/// <c>Вы перенесли запись: салон «Барбершоп»</c>. События мастеру — <c>Новая запись: Анна
/// Петрова</c> / <c>Запись отменена: Анна Петрова</c> и тело <c>Стрижка · сегодня в
/// 14:30 · салон «Барбершоп»</c>. Поэтому они живут в одном месте, а не собираются по месту
/// отправки, и проверяются тестами на точное совпадение.
/// </para>
/// <para>
/// Слово «сегодня»/«завтра» считается по локальной дате <b>салона</b>, а не сервера:
/// напоминание за час до полуночи пересекает сутки, и UTC-дата дала бы неверное слово.
/// Если до визита больше суток (событие создания и событие статуса приходят сразу, а запись —
/// на любой день горизонта 30 дней), слово заменяется датой в родительном падеже:
/// <c>24 ноября</c>. Неизвестный пояс не роняет отправку — время показывается в UTC, как и
/// в напоминании барберу.
/// </para>
/// <para>
/// Отсутствующий снимок просто выпадает из перечисления: заголовок без салона деградирует до
/// «Ваша запись подтверждена» (и двух других фраз), тело без мастера или услуги остаётся
/// читаемым и не содержит «null». В клиентских сообщениях персональных данных нет и быть не
/// может: методы получают только снимок визита (152-ФЗ, решение D3). Событие мастеру
/// добавляет имя клиента — без него мастер не понимает, кто придёт; телефона и заметки
/// по-прежнему нет.
/// </para>
/// <para>
/// Причина отмены в текст не попадает сознательно: строка payload'а не несёт персональных
/// данных, а свободный комментарий барбера мог бы содержать что угодно — для него нет ни
/// колонки снимка, ни проверенного формата. Клиент видит причину в карточке записи (AGENT.md
/// §20.11).
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
    /// Заголовок события «запись подтверждена» без снимка салона. С салоном к этой же фразе
    /// добавляется <c>: салон «…»</c>, поэтому отдельной «полной» константы нет.
    /// </summary>
    public const string CONFIRMED_DEFAULT_TITLE = "Ваша запись подтверждена";

    /// <summary>
    /// Заголовок события «запись отменена» без снимка салона.
    /// </summary>
    public const string CANCELLED_DEFAULT_TITLE = "Ваша запись отменена";

    /// <summary>
    /// Заголовок события «запись перенесена» без снимка салона.
    /// </summary>
    public const string RESCHEDULED_DEFAULT_TITLE = "Ваша запись перенесена";

    /// <summary>
    /// Заголовок события «клиент отменил запись сам» без снимка салона.
    /// </summary>
    public const string CUSTOMER_CANCELLED_DEFAULT_TITLE = "Вы отменили запись";

    /// <summary>
    /// Заголовок события «клиент перенёс визит сам» без снимка салона.
    /// </summary>
    public const string CUSTOMER_RESCHEDULED_DEFAULT_TITLE = "Вы перенесли запись";

    /// <summary>
    /// Заголовок события мастеру «клиент записался», когда имени клиента нет.
    /// </summary>
    public const string BARBER_NEW_BOOKING_DEFAULT_TITLE = "Новая запись";

    /// <summary>
    /// Заголовок события мастеру «запись отменена», когда имени клиента нет.
    /// </summary>
    /// <remarks>
    /// Формулировка нейтральна: отменить запись мог клиент или администратор, а вид 10
    /// их не различает. «Клиент отменил» в случае администратора было бы ложью.
    /// </remarks>
    public const string BARBER_CANCELLED_DEFAULT_TITLE = "Запись отменена";

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
    /// Собирает сообщение по виду уведомления: напоминание, событие создания или событие
    /// статуса брони.
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

            // Все события статуса — про клиента, а не про автора действия: заголовок
            // и тип различаются, тело собирается одинаково (AGENT.md §20.11).
            PushNotificationKind.BookingConfirmed
                or PushNotificationKind.BookingCancelledByBarber
                or PushNotificationKind.BookingRescheduledByBarber
                or PushNotificationKind.BookingCancelledByCustomer
                or PushNotificationKind.BookingRescheduledByCustomer =>
                Status(notification, now),

            // События мастеру — вторая сторона создания и отмены. Текст другой: получатель
            // знает и мастера, и салон, ему нужны клиент, услуга и время.
            PushNotificationKind.BarberBookingCreated =>
                BarberEvent(notification, now, created: true),
            PushNotificationKind.BarberBookingCancelled =>
                BarberEvent(notification, now, created: false),
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
    /// Собирает заголовок события статуса брони; без снимка салона остаётся та же фраза
    /// без суффикса.
    /// </summary>
    /// <param name="kind">Вид события статуса.</param>
    /// <param name="salonName">Снимок салона или <c>null</c>.</param>
    public static string ComposeStatusTitle(PushNotificationKind kind, string? salonName)
    {
        var title = kind switch
        {
            PushNotificationKind.BookingConfirmed => CONFIRMED_DEFAULT_TITLE,
            PushNotificationKind.BookingCancelledByBarber => CANCELLED_DEFAULT_TITLE,
            PushNotificationKind.BookingRescheduledByBarber => RESCHEDULED_DEFAULT_TITLE,
            PushNotificationKind.BookingCancelledByCustomer => CUSTOMER_CANCELLED_DEFAULT_TITLE,
            PushNotificationKind.BookingRescheduledByCustomer => CUSTOMER_RESCHEDULED_DEFAULT_TITLE,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The kind is not a booking status event.")
        };

        return string.IsNullOrWhiteSpace(salonName)
            ? title
            : title + ": салон «" + salonName.Trim() + "»";
    }

    /// <summary>
    /// Собирает заголовок события мастеру: «Новая запись: Анна Петрова» или
    /// «Запись отменена: Анна Петрова»; без имени клиента — та же фраза без суффикса.
    /// </summary>
    /// <param name="kind">
    /// <see cref="PushNotificationKind.BarberBookingCreated"/> или
    /// <see cref="PushNotificationKind.BarberBookingCancelled"/>.
    /// </param>
    /// <param name="customerName">Снимок имени клиента или <c>null</c>.</param>
    public static string ComposeBarberTitle(PushNotificationKind kind, string? customerName)
    {
        var title = kind switch
        {
            PushNotificationKind.BarberBookingCreated => BARBER_NEW_BOOKING_DEFAULT_TITLE,
            PushNotificationKind.BarberBookingCancelled => BARBER_CANCELLED_DEFAULT_TITLE,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The kind is not a barber booking event.")
        };

        return string.IsNullOrWhiteSpace(customerName)
            ? title
            : title + ": " + customerName.Trim();
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
            ComposeVisitBody(notification, now, timeZone),
            notification.BookingId,
            PushMessage.BOOKING_CREATED_TYPE);
    }

    /// <summary>
    /// Собирает событие статуса брони: заголовок и тип зависят от вида, тело — общее.
    /// </summary>
    private static PushMessage Status(PushNotification notification, DateTimeOffset now)
    {
        var timeZone = ResolveTimeZone(notification.SalonTimeZoneId);

        return new PushMessage(
            ComposeStatusTitle(notification.Kind, notification.SalonName),
            ComposeVisitBody(notification, now, timeZone),
            notification.BookingId,
            StatusType(notification.Kind));
    }

    /// <summary>
    /// Собирает событие мастеру о создании записи или её отмене.
    /// </summary>
    /// <remarks>
    /// Тело не начинается с «Мастер …»: получатель и есть мастер, ему нужны клиент, услуга
    /// и время. Имя клиента живёт в заголовке, поэтому тело остаётся коротким и читаемым.
    /// </remarks>
    private static PushMessage BarberEvent(
        PushNotification notification,
        DateTimeOffset now,
        bool created)
    {
        var timeZone = ResolveTimeZone(notification.SalonTimeZoneId);

        return new PushMessage(
            ComposeBarberTitle(notification.Kind, notification.CustomerName),
            ComposeBarberBody(notification, now, timeZone),
            notification.BookingId,
            created
                ? PushMessage.BARBER_BOOKING_CREATED_TYPE
                : PushMessage.BARBER_BOOKING_CANCELLED_TYPE);
    }

    /// <summary>
    /// Тип события статуса для <c>data.type</c>: по нему мобильное приложение выбирает
    /// обработчик перехода (все события статуса открывают «Записи»).
    /// </summary>
    private static string StatusType(PushNotificationKind kind)
        => kind switch
        {
            PushNotificationKind.BookingConfirmed => PushMessage.BOOKING_CONFIRMED_TYPE,
            PushNotificationKind.BookingCancelledByBarber => PushMessage.BOOKING_CANCELLED_TYPE,
            PushNotificationKind.BookingRescheduledByBarber => PushMessage.BOOKING_RESCHEDULED_TYPE,
            PushNotificationKind.BookingCancelledByCustomer
                => PushMessage.BOOKING_CANCELLED_BY_CUSTOMER_TYPE,
            PushNotificationKind.BookingRescheduledByCustomer
                => PushMessage.BOOKING_RESCHEDULED_BY_CUSTOMER_TYPE,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The kind is not a booking status event.")
        };

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
    /// Собирает тело события о визите (создание и статус): мастер, услуга и день со временем;
    /// отсутствующие части выпадают точно так же, как у напоминания.
    /// </summary>
    private static string ComposeVisitBody(
        PushNotification notification,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
        => ComposeVisitBody(
            notification.BarberName,
            notification.ServiceName,
            notification.StartAt,
            now,
            timeZone);

    /// <summary>
    /// Собирает тело письма о визите по снимку каталога: та же фраза, что у push-события
    /// статуса, — письмо и уведомление должны говорить одно и то же.
    /// </summary>
    /// <param name="snapshot">Снимок салона, мастера, услуги и пояса.</param>
    /// <param name="startAt">Время визита.</param>
    /// <param name="now">Текущий момент в UTC.</param>
    public static string ComposeVisitBody(
        PushBookingSnapshot snapshot,
        DateTimeOffset startAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return ComposeVisitBody(
            snapshot.BarberName,
            snapshot.ServiceName,
            startAt,
            now,
            ResolveTimeZone(snapshot.SalonTimeZoneId));
    }

    /// <summary>
    /// Общая сборка тела: части снимка выпадают по отдельности, «null» в тексте не появляется.
    /// </summary>
    private static string ComposeVisitBody(
        string? barberName,
        string? serviceName,
        DateTimeOffset startAt,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(barberName))
        {
            builder.Append("Мастер ").Append(barberName.Trim()).Append(PART_SEPARATOR);
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            builder.Append(serviceName.Trim()).Append(PART_SEPARATOR);
        }

        builder
            .Append(CreationDayWord(startAt, now, timeZone))
            .Append(" в ")
            .Append(SalonTimeZone.FormatTimeOfDay(startAt, timeZone));

        return builder.ToString();
    }

    /// <summary>
    /// Собирает тело события мастеру: услуга, день и время, салон; отсутствующие части
    /// выпадают по отдельности.
    /// </summary>
    /// <remarks>
    /// Вид: <c>Стрижка · сегодня в 14:30 · салон «Барбершоп»</c>. Салон идёт последним:
    /// мастер обычно работает в одном месте, а услуга и время — главное. Слово
    /// «сегодня»/«завтра» и дата в родительном падеже считаются по поясу салона, как у
    /// клиентских событий.
    /// </remarks>
    private static string ComposeBarberBody(
        PushNotification notification,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(notification.ServiceName))
        {
            builder.Append(notification.ServiceName.Trim()).Append(PART_SEPARATOR);
        }

        builder
            .Append(CreationDayWord(notification.StartAt, now, timeZone))
            .Append(" в ")
            .Append(SalonTimeZone.FormatTimeOfDay(notification.StartAt, timeZone));

        if (!string.IsNullOrWhiteSpace(notification.SalonName))
        {
            builder
                .Append(PART_SEPARATOR)
                .Append("салон «")
                .Append(notification.SalonName.Trim())
                .Append('»');
        }

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
    /// Выбирает день события о визите: сегодня, завтра или дату в родительном падеже.
    /// </summary>
    /// <remarks>
    /// Событие приходит сразу после действия, а сам визит может быть на любой день горизонта
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
