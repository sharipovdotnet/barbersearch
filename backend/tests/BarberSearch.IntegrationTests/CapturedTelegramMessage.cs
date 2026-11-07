namespace BarberSearch.IntegrationTests;

/// <summary>
/// Одно отправленное сообщение: чат и точный текст.
/// </summary>
/// <param name="ChatId">Идентификатор чата.</param>
/// <param name="Text">Текст, который получил бы Telegram.</param>
public sealed record CapturedTelegramMessage(long ChatId, string Text);
