using System.Text.Json.Serialization;

namespace BarberSearch.Web.Notifications;

/// <summary>
/// Чат Telegram: идентификатор, по которому бот отправляет сообщения.
/// </summary>
/// <param name="Id">Идентификатор чата; хранится в настройках барбера.</param>
public sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id);
