using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IEmailSender"/>: запоминает письма вместо SMTP.
/// </summary>
/// <remarks>
/// Обработчики запросов больше не шлют письма: <see cref="Messages"/> заполняется только
/// диспетчером outbox. Признак готовности почты — тот же объект, поэтому обработчики получают
/// его как <see cref="IEmailSenderAvailability"/>, а тесты выключают почту одним свойством.
/// </remarks>
public sealed class FakeEmailSender : IEmailSender, IEmailSenderAvailability
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages => _messages;

    /// <summary>
    /// Готовность транспорта: тест выключает почту, не собирая реальные настройки.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Сколько раз диспетчер пытался отправить письмо.
    /// </summary>
    public int SendCallCount { get; private set; }

    /// <summary>
    /// Подмена результата отправки: тест проверяет ретраи диспетчера.
    /// </summary>
    public Func<EmailMessage, UnitResult<Failure>>? Behavior { get; set; }

    public Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        SendCallCount++;
        _messages.Add(message);

        return Task.FromResult(
            Behavior?.Invoke(message) ?? UnitResult.Success<Failure>());
    }
}
