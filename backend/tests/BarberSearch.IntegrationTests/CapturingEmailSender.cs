using System.Collections.Concurrent;
using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Подменный отправитель писем: запоминает письма вместо SMTP.
/// </summary>
/// <remarks>
/// Интеграционный тест обязан подменять <see cref="IEmailSender"/>, а не ходить в настоящий
/// SMTP. Регистрируется через <see cref="TestApp"/> в <c>ConfigureTestServices</c>.
/// </remarks>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmailMessage> _messages = new();

    /// <summary>
    /// Отправленные письма в порядке отправки.
    /// </summary>
    public IReadOnlyList<CapturedEmailMessage> Messages => _messages.ToArray();

    /// <summary>
    /// Готовность транспорта; по умолчанию настроен, как в реальном рабочем окружении.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Подмена результата отправки: тест проверяет <c>500 auth.emailVerification.sendFailed</c>.
    /// </summary>
    public Func<EmailMessage, UnitResult<Failure>>? Behavior { get; set; }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(new CapturedEmailMessage(
            message.To,
            message.Subject,
            message.HtmlBody,
            message.TextBody));

        return Task.FromResult(
            Behavior?.Invoke(message) ?? UnitResult.Success<Failure>());
    }
}
