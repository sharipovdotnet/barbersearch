using BarberSearch.Contracts.Notifications;
using BarberSearch.Domain.Notifications;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка инбокса уведомлений мастера: заголовок, вторая строка, момент и признак прочтения.
/// </summary>
/// <remarks>
/// <para>
/// Заголовок и вторая строка собираются общими форматтерами
/// <see cref="BarberProfileText.NotificationTitle"/> и
/// <see cref="BarberProfileText.NotificationBody"/>: те же слова уже показывают колокольчик
/// и push, и второй набор формулировок разошёлся бы с ними. Момент читается в поясе салона
/// из ответа, а если сервер его не прислал — в поясе телефона: у напоминания нет другого
/// разумного пояса, и UTC на телефоне выглядел бы случайным.
/// </para>
/// <para>
/// «Через сколько» берётся из самих данных, а не из настроек напоминаний: уведомление
/// создаётся ровно за <c>leadMinutes</c> до начала, поэтому разница <c>startAt</c> и
/// <c>createdAt</c> — это и есть срок. Настройки уведомлений в этот срез не входят, и
/// лишнее чтение ради одной подписи здесь не нужно; если разница неправдоподобна,
/// используется серверное умолчание 30 минут.
/// </para>
/// <para>
/// <see cref="IsUnread"/> меняется после успешной отметки «прочитано»: строка обязана
/// погасить точку сразу, не дожидаясь перечитывания страницы. Сам запрос остаётся за
/// <c>BarberNotificationsViewModel</c>.
/// </para>
/// </remarks>
public sealed partial class BarberNotificationItem : ObservableObject
{
    /// <summary>
    /// Срок напоминания по умолчанию: столько минут сервер ставит при создании настроек.
    /// </summary>
    private const int DefaultLeadMinutes = 30;

    /// <summary>
    /// Верхняя граница правдоподобного срока: напоминание не создаётся за четверо суток.
    /// </summary>
    private const int MaxPlausibleLeadMinutes = 240;

    /// <summary>
    /// Создаёт строку из уведомления API.
    /// </summary>
    /// <param name="notification">Уведомление из инбокса.</param>
    /// <param name="now">Текущий момент: от него зависят «Сегодня» и «Вчера».</param>
    public BarberNotificationItem(BarberNotificationResponse notification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        Id = notification.Id;
        Kind = notification.Kind;
        BookingId = notification.BookingId;
        IsUnread = notification.ReadAt is null;
        Title = BarberProfileText.NotificationTitle(
            notification,
            ResolveLeadMinutes(notification));
        Body = BarberProfileText.NotificationBody(notification);

        var timeZone = SalonClock.TryResolve(notification.SalonTimeZoneId) ?? TimeZoneInfo.Local;

        MomentText = BarberProfileText.NotificationTime(notification.CreatedAt, now, timeZone);
    }

    /// <summary>
    /// Идентификатор уведомления: по нему отмечается прочтение.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Вид уведомления: напоминание о визите или утренний дайджест.
    /// </summary>
    public BarberNotificationKind Kind { get; }

    /// <summary>
    /// Запись, к которой относится напоминание; у дайджеста <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Отдельного экрана записи в телефоне нет, поэтому идентификатор не ведёт на визит:
    /// строка открывает раздел «Записи» целиком, как и в вебе.
    /// </remarks>
    public Guid? BookingId { get; }

    /// <summary>
    /// Заголовок строки: «Через 30 минут запись» или «Сегодня 2 записи».
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Вторая строка: время, услуга и салон либо первая запись дня.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Момент уведомления: «Сегодня, 14:02», «Вчера, 09:15», «15 ноября, 14:02».
    /// </summary>
    public string MomentText { get; }

    /// <summary>
    /// Вторая строка не пуста: у дайджеста без времени её нет.
    /// </summary>
    public bool HasBody => Body.Length > 0;

    /// <summary>
    /// Напоминание о визите, а не утренний дайджест.
    /// </summary>
    public bool IsUpcoming => Kind == BarberNotificationKind.Upcoming;

    /// <summary>
    /// Дайджест за день, а не напоминание о визите.
    /// </summary>
    public bool IsDigest => Kind == BarberNotificationKind.Digest;

    /// <summary>
    /// Короткий ключ вида для класса строки в разметке: <c>upcoming</c> или <c>digest</c>.
    /// </summary>
    public string KindKey => IsUpcoming ? "upcoming" : "digest";

    /// <summary>
    /// У строки есть связанная запись, даже если экран не открывает её напрямую.
    /// </summary>
    public bool HasBooking => BookingId is not null;

    /// <summary>
    /// Уведомление ещё не прочитано: точка и подсветка строки.
    /// </summary>
    [ObservableProperty]
    private bool _isUnread;

    /// <summary>
    /// Помечает строку прочитанной без обращения к серверу.
    /// </summary>
    /// <remarks>
    /// Нужно после успешного <c>POST …/read</c> и после <c>404</c>: удалённое уведомление
    /// нельзя прочитать, но и держать точку непрочитанного незачем.
    /// </remarks>
    public void MarkRead() => IsUnread = false;

    /// <summary>
    /// Считает срок напоминания по паре «создано — начало визита».
    /// </summary>
    /// <param name="notification">Уведомление из ответа.</param>
    /// <returns>Срок в минутах либо серверное умолчание.</returns>
    private static int ResolveLeadMinutes(BarberNotificationResponse notification)
    {
        if (notification.StartAt is not { } startAt)
        {
            return DefaultLeadMinutes;
        }

        var minutes = (int)Math.Round(
            (startAt - notification.CreatedAt).TotalMinutes,
            MidpointRounding.AwayFromZero);

        return minutes is > 0 and <= MaxPlausibleLeadMinutes
            ? minutes
            : DefaultLeadMinutes;
    }
}
