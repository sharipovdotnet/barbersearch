namespace BarberSearch.Contracts.Notifications;

/// <summary>
/// Настройки напоминаний барбера.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DigestTime"/> — строка <c>HH:mm</c>, а не сериализация <see cref="TimeOnly"/>
/// (она дала бы <c>08:00:00</c>): значение подставляется в <c>&lt;input type="time"&gt;</c>
/// без преобразований.
/// </para>
/// <para>
/// Идентификатор чата Telegram наружу не отдаётся: клиенту достаточно
/// <see cref="TelegramLinked"/> и <see cref="TelegramLinkedAt"/>, а сам chatId — внутренняя
/// деталь канала. <see cref="BotConfigured"/> говорит, настроен ли бот на сервере: при
/// <c>false</c> кнопка «Подключить» не работает, и клиент объясняет почему.
/// </para>
/// </remarks>
/// <param name="IsEnabled">Присылать ли напоминания вообще; выключает и инбокс, и доставку.</param>
/// <param name="DigestEnabled">Присылать ли утренний дайджест.</param>
/// <param name="DigestTime">Время дайджеста в поясе основного салона, <c>HH:mm</c>.</param>
/// <param name="LeadMinutes">За сколько минут до визита напоминать: 15, 30 или 60.</param>
/// <param name="TelegramLinked">Привязан ли чат Telegram.</param>
/// <param name="TelegramLinkedAt">Когда чат привязан; <c>null</c> — не привязан.</param>
/// <param name="BotConfigured">Настроен ли бот Telegram на сервере (токен и имя бота заданы).</param>
public record NotificationSettingsResponse(
    bool IsEnabled,
    bool DigestEnabled,
    string DigestTime,
    int LeadMinutes,
    bool TelegramLinked,
    DateTimeOffset? TelegramLinkedAt,
    bool BotConfigured);
