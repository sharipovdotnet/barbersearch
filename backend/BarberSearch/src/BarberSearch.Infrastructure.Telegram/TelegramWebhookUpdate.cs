using System.Text.Json.Serialization;

namespace BarberSearch.Infrastructure.Telegram;

/// <summary>
/// Обновление Telegram Bot API, пришедшее на вебхук.
/// </summary>
/// <remarks>
/// <para>
/// Разбирается только то, что нужно привязке: идентификатор обновления и сообщение с чатом.
/// Остальные поля Telegram игнорируются — модель Bot API целиком не нужна и не должна
/// проникать в Application.
/// </para>
/// <para>
/// Суффикс <c>Webhook</c> отделяет wire-модель от EF-сущности строки идемпотентности
/// <c>telegram_updates</c> (<c>BarberSearch.Infrastructure.Postgresql.Telegram.TelegramUpdate</c>):
/// это разные типы, и одинаковое имя делало бы импорт обоих неймспейсов неоднозначным.
/// </para>
/// </remarks>
/// <param name="UpdateId">Уникальный идентификатор обновления; ключ идемпотентности.</param>
/// <param name="Message">Сообщение, если обновление — сообщение.</param>
public sealed record TelegramWebhookUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);
