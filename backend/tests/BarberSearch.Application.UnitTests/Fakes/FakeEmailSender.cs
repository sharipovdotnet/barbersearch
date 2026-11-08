using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IEmailSender"/>: запоминает письма вместо SMTP.
/// </summary>
/// <remarks>
/// Тесты проверяют ссылку в письме и отсутствие отправки при ошибках валидации/кулдауна.
/// <see cref="IsInTransaction"/> позволяет убедиться, что письмо уходит **после** коммита.
/// </remarks>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages => _messages;

    /// <summary>
    /// Проверка «мы внутри транзакции»; тест подключает сюда <see cref="FakeUnitOfWork"/>.
    /// </summary>
    public Func<bool>? IsInTransaction { get; set; }

    /// <summary>
    /// Был ли <see cref="SendAsync"/> вызван внутри транзакции.
    /// </summary>
    public bool SentInTransaction { get; private set; }

    /// <summary>
    /// Подмена результата отправки: тест проверяет <c>500 auth.emailVerification.sendFailed</c>.
    /// </summary>
    public Func<EmailMessage, UnitResult<Failure>>? Behavior { get; set; }

    public Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(message);
        SentInTransaction |= IsInTransaction?.Invoke() ?? false;

        return Task.FromResult(
            Behavior?.Invoke(message) ?? UnitResult.Success<Failure>());
    }
}
