namespace BarberSearch.Infrastructure.Postgresql.Telegram;

/// <summary>
/// Строка идемпотентности вебхука Telegram: один обработанный <c>update_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// Таблица принадлежит инфраструктуре, а не домену: это технический журнал доставки
/// обновлений от Bot API, у него нет бизнес-правил и он не участвует в модели уведомлений.
/// Уникальный первичный ключ <see cref="UpdateId"/> — единственная защита от повторной
/// обработки: Telegram ретраит обновление, пока не получит <c>2xx</c>, и при двух
/// экземплярах приложения <c>INSERT ... ON CONFLICT DO NOTHING</c> решает, кто обработает
/// обновление первым.
/// </para>
/// <para>
/// Хранится минимальная пара: идентификатор обновления и момент приёма. Текст сообщения,
/// чат и код привязки не сохраняются — после обработки они не нужны.
/// </para>
/// </remarks>
public sealed class TelegramUpdate
{
    /// <summary>
    /// Идентификатор обновления Telegram; он же первичный ключ.
    /// </summary>
    public long UpdateId { get; private set; }

    /// <summary>
    /// Момент, когда экземпляр приложения принял обновление (UTC).
    /// </summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    private TelegramUpdate()
    {
    }

    /// <summary>
    /// Создаёт строку журнала.
    /// </summary>
    /// <param name="updateId">Идентификатор обновления из Bot API.</param>
    /// <param name="receivedAt">Момент приёма в UTC — часы Application.</param>
    public static TelegramUpdate Create(long updateId, DateTimeOffset receivedAt)
        => new()
        {
            UpdateId = updateId,
            ReceivedAt = receivedAt
        };
}
