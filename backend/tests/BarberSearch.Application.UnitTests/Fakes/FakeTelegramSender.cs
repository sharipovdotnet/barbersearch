using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Фейковый телеграм-канал: запоминает вызовы и отдаёт заранее заданный исход.
/// </summary>
/// <remarks>
/// Исход задаётся полем <see cref="Result"/>, чтобы один и тот же обработчик проверить
/// на успехе, на «чат недоступен» (<c>403</c>) и на временной ошибке. Текст сохраняется
/// целиком: тест проверяет, что в нём нет имени и телефона клиента.
/// </remarks>
public sealed class FakeTelegramSender : ITelegramSender
{
    private readonly List<(long ChatId, string Text)> _calls = [];

    public IReadOnlyList<(long ChatId, string Text)> Calls => _calls;

    /// <summary>
    /// Исход следующей отправки; по умолчанию — успех.
    /// </summary>
    public UnitResult<Failure> Result { get; set; } = UnitResult.Success<Failure>();

    public Task<UnitResult<Failure>> SendMessageAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((chatId, text));

        return Task.FromResult(Result);
    }

    /// <summary>
    /// Исход «чат недоступен»: Telegram ответил <c>403</c> — бот заблокирован.
    /// </summary>
    public static Failure ChatUnavailable()
        => new(Error.Failure(
            ApiErrorCodes.Notification.TelegramChatUnavailable,
            "Telegram rejected the chat: 403 Forbidden."));

    /// <summary>
    /// Исход «временная ошибка»: сеть или <c>5xx</c>, попытку нужно повторить.
    /// </summary>
    public static Failure TemporaryFailure()
        => new(Error.Failure(
            ApiErrorCodes.Notification.TelegramSendFailed,
            "Telegram send failed: 502 Bad Gateway."));
}
