using System.Collections.Concurrent;
using BarberSearch.Application.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Подменный Telegram: запоминает отправленные сообщения вместо HTTP-запроса.
/// </summary>
/// <remarks>
/// Так интеграционный тест проверяет байт-в-байт текст и отсутствие ПДн, не обращаясь
/// к Bot API. Регистрируется в <see cref="TestApp"/> вместо настоящего
/// <c>TelegramSender</c> через <c>ConfigureTestServices</c>.
/// </remarks>
public sealed class CapturingTelegramSender : ITelegramSender
{
    private readonly ConcurrentQueue<CapturedTelegramMessage> _messages = new();

    /// <summary>
    /// Отправленные сообщения в порядке отправки.
    /// </summary>
    public IReadOnlyList<CapturedTelegramMessage> Messages => _messages.ToArray();

    /// <inheritdoc />
    public Task<UnitResult<Failure>> SendMessageAsync(
        long chatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(new CapturedTelegramMessage(chatId, text));

        return Task.FromResult(UnitResult.Success<Failure>());
    }
}
