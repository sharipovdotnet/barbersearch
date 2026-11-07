namespace BarberSearch.Contracts.Notifications.CreateTelegramLink;

/// <summary>
/// Ответ на запрос привязки Telegram: одноразовый код и готовая ссылка на бота.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> показывается в кабинете, чтобы код можно было продиктовать и ввести
/// вручную, если ссылка не открылась. <see cref="DeepLink"/> ведёт в бота с параметром
/// <c>start</c>: Telegram сам отправит боту <c>/start {code}</c>, и вебхук привяжет чат.
/// </para>
/// <para>
/// Код одноразовый и живёт 10 минут; после привязки или истечения он не сработает, поэтому
/// клиент не должен кэшировать ответ дольше, чем до <see cref="ExpiresAt"/>.
/// </para>
/// </remarks>
/// <param name="Code">Одноразовый код привязки (32 символа base32).</param>
/// <param name="DeepLink">Ссылка <c>https://t.me/{bot}?start={code}</c>.</param>
/// <param name="ExpiresAt">Срок действия кода в UTC.</param>
public record TelegramLinkResponse(
    string Code,
    string DeepLink,
    DateTimeOffset ExpiresAt);
