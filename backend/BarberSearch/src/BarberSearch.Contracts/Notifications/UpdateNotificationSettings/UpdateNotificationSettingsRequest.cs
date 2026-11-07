namespace BarberSearch.Contracts.Notifications.UpdateNotificationSettings;

/// <summary>
/// Данные запроса на сохранение настроек напоминаний.
/// </summary>
/// <remarks>
/// <para>
/// <c>PUT</c> заменяет объект целиком, поэтому все четыре поля обязательны по смыслу:
/// пропущенное <see cref="DigestTime"/> — ошибка <c>400 notificationSettings.digestTime</c>,
/// а не «оставить как было». Это позволяет клиенту отправлять одно состояние и не
/// собирать его из частичных правок.
/// </para>
/// <para>
/// Формат времени — <c>HH:mm</c> (строка, а не <see cref="TimeOnly"/>): то же значение, что
/// отдаёт <c>NotificationSettingsResponse</c> и что принимает <c>&lt;input type="time"&gt;</c>.
/// Биндер не проверяет формат — его разбирает обработчик и отвечает публичным кодом.
/// </para>
/// </remarks>
/// <param name="IsEnabled">Присылать ли напоминания вообще.</param>
/// <param name="DigestEnabled">Присылать ли утренний дайджест.</param>
/// <param name="DigestTime">Время дайджеста, <c>HH:mm</c>; 06:00..12:00 с шагом 15 минут.</param>
/// <param name="LeadMinutes">За сколько минут до визита напоминать: 15, 30 или 60.</param>
public record UpdateNotificationSettingsRequest(
    bool IsEnabled,
    bool DigestEnabled,
    string? DigestTime,
    int LeadMinutes);
