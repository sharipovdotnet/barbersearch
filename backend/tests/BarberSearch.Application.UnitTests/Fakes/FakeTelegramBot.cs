using BarberSearch.Application.Notifications;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Фейковый бот: признак готовности канала и ссылка привязки без настроек Bot API.
/// </summary>
/// <remarks>
/// Заменяет <c>TelegramOptions</c> в тестах Application: обработчики видят только порт
/// <see cref="ITelegramBot"/>, поэтому ни токен, ни секрет вебхука им не нужны.
/// </remarks>
public sealed class FakeTelegramBot : ITelegramBot
{
    private readonly string _botUsername;

    public FakeTelegramBot(bool isConfigured = true, string botUsername = "barbersearch_bot")
    {
        IsConfigured = isConfigured;
        _botUsername = botUsername;
    }

    /// <inheritdoc />
    public bool IsConfigured { get; }

    /// <inheritdoc />
    public string BuildDeepLink(string code)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Cannot build a Telegram deep link for an unconfigured fake bot.");
        }

        return $"https://t.me/{_botUsername}?start={code}";
    }
}
