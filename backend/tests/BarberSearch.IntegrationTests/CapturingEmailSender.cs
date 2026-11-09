using System.Collections.Concurrent;
using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Подменный отправитель писем: запоминает письма вместо SMTP.
/// </summary>
/// <remarks>
/// <para>
/// Интеграционный тест обязан подменять <see cref="IEmailSender"/>, а не ходить в настоящий
/// SMTP. Регистрируется через <see cref="TestApp"/> в <c>ConfigureTestServices</c>; тот же
/// экземпляр отвечает и за готовность почты (<see cref="IEmailSenderAvailability"/>), поэтому
/// <see cref="IsAvailable"/> заменяет реальную секцию <c>Email</c>.
/// </para>
/// <para>
/// Письмо попадает в <see cref="Messages"/> только при успешной доставке: тест на ретраи
/// видит, сколько писем реально принял транспорт. <see cref="Delay"/> искусственно
/// растягивает отправку — так проверяется, что два параллельных тика не отправляют одну
/// строку дважды.
/// </para>
/// </remarks>
public sealed class CapturingEmailSender : IEmailSender, IEmailSenderAvailability
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
    /// Задержка отправки: тест выравнивает по ней два параллельных тика.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Подмена результата отправки: тест проверяет ретраи и окончательный отказ диспетчера.
    /// </summary>
    public Func<EmailMessage, UnitResult<Failure>>? Behavior { get; set; }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        var result = Behavior?.Invoke(message) ?? UnitResult.Success<Failure>();

        if (result.IsFailure)
        {
            return result;
        }

        _messages.Enqueue(new CapturedEmailMessage(
            message.To,
            message.Subject,
            message.HtmlBody,
            message.TextBody));

        return result;
    }
}
