using System.Text.Json.Serialization;

namespace BarberSearch.Infrastructure.Telegram;

/// <summary>
/// Сообщение Telegram, из которого берутся чат и текст команды.
/// </summary>
/// <param name="Text">Текст сообщения; для привязки — <c>/start {code}</c>.</param>
/// <param name="Chat">Чат, из которого пришло сообщение.</param>
public sealed record TelegramMessage(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("chat")] TelegramChat? Chat);
