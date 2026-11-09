using System.Text.Json.Serialization;

namespace BarberSearch.Infrastructure.Telegram;

/// <summary>
/// Обновление Telegram Bot API, пришедшее на вебхук.
/// </summary>
/// <remarks>
/// Разбирается только то, что нужно привязке: идентификатор обновления и сообщение с чатом.
/// Остальные поля Telegram игнорируются — модель Bot API целиком не нужна и не должна
/// проникать в Application.
/// </remarks>
/// <param name="UpdateId">Уникальный идентификатор обновления; ключ идемпотентности.</param>
/// <param name="Message">Сообщение, если обновление — сообщение.</param>
public sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);
