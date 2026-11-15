using System.Text;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Push;

namespace BarberSearch.Application.Push;

/// <summary>
/// Русские тексты push-напоминаний клиенту.
/// </summary>
/// <remarks>
/// <para>
/// Тексты — часть наблюдаемого поведения и приходят байт в байт: заголовок
/// <c>Через час: салон «Барбершоп»</c>, тело <c>Мастер Иван · Стрижка · сегодня в 14:30</c>.
/// Поэтому они живут в одном месте, а не собираются по месту отправки, и проверяются
/// тестами на точное совпадение.
/// </para>
/// <para>
/// Слово «сегодня»/«завтра» считается по локальной дате <b>салона</b>, а не сервера:
/// напоминание за час до полуночи пересекает сутки, и UTC-дата дала бы неверное слово.
/// Неизвестный пояс не роняет отправку — время показывается в UTC, как и в напоминании
/// барберу.
/// </para>
/// <para>
/// Отсутствующий снимок просто выпадает из перечисления: заголовок без салона деградирует
/// до <see cref="DEFAULT_TITLE"/>, тело без мастера или услуги остаётся читаемым и не
/// содержит «null». Персональных данных клиента здесь нет и быть не может: метод получает
/// только снимок визита (152-ФЗ, решение D3).
/// </para>
/// </remarks>
public static class PushMessages
{
    /// <summary>
    /// Заголовок, когда салон неизвестен.
    /// </summary>
    public const string DEFAULT_TITLE = "Через час — визит к мастеру";

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

        var timeZone = SalonTimeZone.TryResolve(notification.SalonTimeZoneId, out var resolved)
            ? resolved
            : TimeZoneInfo.Utc;

        return new PushMessage(
            ComposeTitle(notification.SalonName),
            ComposeBody(notification, now, timeZone),
            notification.BookingId);
    }

    /// <summary>
    /// Собирает заголовок: с салоном — «Через час: салон «…»», без него — деградированный.
    /// </summary>
    /// <param name="salonName">Снимок салона или <c>null</c>.</param>
    public static string ComposeTitle(string? salonName)
        => string.IsNullOrWhiteSpace(salonName)
            ? DEFAULT_TITLE
            : "Через час: салон «" + salonName.Trim() + "»";

    /// <summary>
    /// Собирает тело: мастер, услуга и день со временем; отсутствующие части выпадают.
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
}
