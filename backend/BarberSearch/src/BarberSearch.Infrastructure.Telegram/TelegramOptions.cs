using BarberSearch.Application.Notifications;

namespace BarberSearch.Infrastructure.Telegram;

/// <summary>
/// Настройки Telegram-бота — секция конфигурации <c>Telegram</c>.
/// </summary>
/// <remarks>
/// <para>
/// Транспортные настройки Bot API: Application их не видит и знает только порты
/// <see cref="ITelegramBot"/> и <see cref="ITelegramSender"/>. Заполняется композиционным
/// корнем Web и регистрируется синглтоном через <see cref="TelegramSenderExtensions.AddTelegramSender"/>,
/// как <c>EmailOptions</c>. Токен и секрет вебхука — секреты: они приходят из user secrets или
/// переменных окружения и никогда не попадают в ответы API.
/// </para>
/// <para>
/// Пустой токен — нормальное состояние разработки: канал выключен, инбокс работает.
/// Поэтому ни один обработчик не падает без токена (§6.1, критерий приёмки 3).
/// </para>
/// </remarks>
public sealed record TelegramOptions : ITelegramBot
{
    /// <summary>
    /// Имя секции конфигурации: <c>Telegram</c>.
    /// </summary>
    public const string SECTION = "Telegram";

    /// <summary>
    /// Токен бота; <c>Telegram:BotToken</c>. Пустой — канал не настроен.
    /// </summary>
    public string? BotToken { get; init; }

    /// <summary>
    /// Имя бота без <c>@</c>; <c>Telegram:BotUsername</c>. Из него собирается ссылка
    /// <c>https://t.me/{bot}?start={code}</c>.
    /// </summary>
    public string? BotUsername { get; init; }

    /// <summary>
    /// Ожидаемое значение заголовка <c>X-Telegram-Bot-Api-Secret-Token</c>; <c>Telegram:WebhookSecret</c>.
    /// </summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// Настроен ли канал: есть и токен, и имя бота. Клиент видит это как <c>botConfigured</c>
    /// и не показывает рабочую кнопку «Подключить», пока канал не настроен на сервере.
    /// </summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(BotUsername);

    /// <inheritdoc />
    public string BuildDeepLink(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var botUsername = BotUsername?.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(botUsername))
        {
            throw new InvalidOperationException(
                "Cannot build a Telegram deep link without the bot username.");
        }

        return $"https://t.me/{botUsername}?start={code}";
    }
}
