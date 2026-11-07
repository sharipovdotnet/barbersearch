using System.Text;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Тексты уведомлений и ответов бота.
/// </summary>
/// <remarks>
/// <para>
/// Тексты — часть наблюдаемого поведения и совпадают с §6.2 спецификации варианта C байт
/// в байт, поэтому живут в одном месте, а не собираются по месту отправки. Язык — русский:
/// это сообщение барберу, а не лог.
/// </para>
/// <para>
/// Персональных данных клиента в тексте нет и быть не может (решение D3, 152-ФЗ): только
/// время, услуга, салон и ссылка в кабинет. Имя, телефон и заметку клиента сюда не передают
/// даже параметром — иначе однажды их передадут.
/// </para>
/// </remarks>
public static class NotificationMessages
{
    /// <summary>
    /// Ответ бота на успешную привязку чата.
    /// </summary>
    public const string TELEGRAM_LINKED = "Готово! Напоминания будут приходить сюда.";

    /// <summary>
    /// Ответ бота, когда код привязки не найден или истёк.
    /// </summary>
    public const string TELEGRAM_LINK_CODE_EXPIRED = "Код устарел. Получите новый в кабинете.";

    /// <summary>
    /// Ответ бота, когда чат уже привязан к другому кабинету.
    /// </summary>
    public const string TELEGRAM_CHAT_ALREADY_LINKED = "Этот чат уже подключён к другому кабинету.";

    /// <summary>
    /// Путь страницы уведомлений в кабинете барбера.
    /// </summary>
    public const string NOTIFICATIONS_PATH = "/barber/notifications";

    /// <summary>
    /// Собирает ссылку в кабинет: публичный корень приложения плюс путь страницы.
    /// </summary>
    /// <remarks>
    /// Пустой <paramref name="publicUrl"/> даёт относительную ссылку, а не падение:
    /// в разработке публичный адрес может быть не настроен, и напоминание всё равно уйдёт.
    /// </remarks>
    /// <param name="publicUrl">Публичный корень приложения из <c>App:PublicUrl</c>.</param>
    public static string CabinetUrl(string? publicUrl)
    {
        var root = publicUrl?.Trim().TrimEnd('/');

        return string.IsNullOrEmpty(root) ? NOTIFICATIONS_PATH : root + NOTIFICATIONS_PATH;
    }

    /// <summary>
    /// Текст напоминания о визите.
    /// </summary>
    /// <remarks>
    /// Вид: <c>Через 30 минут запись: 14:00, стрижка, «Барбершоп на Ленина». Открыть кабинет: {url}</c>.
    /// Отсутствующий снимок услуги или салона просто выпадает из перечисления — текст остаётся
    /// читаемым, а не содержит «null».
    /// </remarks>
    /// <param name="leadMinutes">За сколько минут до визита пришло напоминание.</param>
    /// <param name="localTime">Время начала в поясе салона, <c>HH:mm</c>.</param>
    /// <param name="serviceName">Снимок услуги или <c>null</c>.</param>
    /// <param name="salonName">Снимок салона или <c>null</c>.</param>
    /// <param name="cabinetUrl">Ссылка в кабинет.</param>
    public static string Upcoming(
        int leadMinutes,
        string localTime,
        string? serviceName,
        string? salonName,
        string cabinetUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localTime);
        ArgumentException.ThrowIfNullOrWhiteSpace(cabinetUrl);

        var builder = new StringBuilder();

        builder.Append("Через ").Append(leadMinutes).Append(" минут запись: ").Append(localTime);

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            builder.Append(", ").Append(serviceName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(salonName))
        {
            builder.Append(", «").Append(salonName.Trim()).Append('»');
        }

        builder.Append(". Открыть кабинет: ").Append(cabinetUrl);

        return builder.ToString();
    }

    /// <summary>
    /// Текст утреннего дайджеста.
    /// </summary>
    /// <remarks>
    /// Вид: <c>Сегодня 4 записи, первая в 10:00. Открыть кабинет: {url}</c>. Число записей
    /// подставляется как есть: дайджест создаётся только при <c>BookingsCount &gt; 0</c>.
    /// </remarks>
    /// <param name="bookingsCount">Число подтверждённых записей за локальные сутки салона.</param>
    /// <param name="firstLocalTime">Время первой записи в поясе салона, <c>HH:mm</c>, или <c>null</c>.</param>
    /// <param name="cabinetUrl">Ссылка в кабинет.</param>
    public static string Digest(int bookingsCount, string? firstLocalTime, string cabinetUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cabinetUrl);

        var builder = new StringBuilder();

        builder.Append("Сегодня ").Append(bookingsCount).Append(" записи");

        if (!string.IsNullOrWhiteSpace(firstLocalTime))
        {
            builder.Append(", первая в ").Append(firstLocalTime);
        }

        builder.Append(". Открыть кабинет: ").Append(cabinetUrl);

        return builder.ToString();
    }
}
