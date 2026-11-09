using System.Reflection;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Emails;
using BarberSearch.Domain.EmailOutbox;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IEmailOutboxRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Чтение пачки возвращает сами строки «таблицы»: диспетчер мутирует их, а откат
/// моделируется снимком <see cref="IFakeTransactionParticipant"/> — как настоящая
/// транзакция PostgreSQL.
/// </para>
/// <para>
/// Уникальность <c>deduplication_key</c> моделируется в <see cref="AddAsync"/>: вторая строка
/// с тем же ключом даёт <c>409 emailOutbox.add.uniqueViolation</c>.
/// </para>
/// </remarks>
public sealed class FakeEmailOutboxRepository : IEmailOutboxRepository, IFakeTransactionParticipant
{
    private readonly Dictionary<Guid, EmailOutboxMessage> _messages = [];
    private Dictionary<Guid, EmailOutboxMessage>? _snapshot;

    /// <summary>
    /// Строки «таблицы» в порядке постановки в очередь.
    /// </summary>
    public IReadOnlyCollection<EmailOutboxMessage> Rows => _messages.Values
        .OrderBy(message => message.NextAttemptAt)
        .ThenBy(message => message.CreatedAt)
        .ToList();

    /// <summary>
    /// Письма, которые лежат в outbox, в виде, который получит транспорт.
    /// </summary>
    public IReadOnlyList<EmailMessage> Messages => Rows
        .Select(message => new EmailMessage(
            message.ToEmail,
            message.Subject,
            message.HtmlBody,
            message.TextBody))
        .ToList();

    /// <summary>
    /// Проверка «мы внутри транзакции»; тест подключает сюда <see cref="FakeUnitOfWork"/>.
    /// </summary>
    public Func<bool>? IsInTransaction { get; set; }

    /// <summary>
    /// Был ли <see cref="AddAsync"/> вызван внутри транзакции.
    /// </summary>
    public bool AddInTransaction { get; private set; }

    public int AddCallCount { get; private set; }

    /// <summary>
    /// Подмена вставки: тест проверяет откат токена при сбое строки outbox.
    /// </summary>
    public Func<EmailOutboxMessage, UnitResult<Failure>>? AddBehavior { get; set; }

    public void AddRaw(EmailOutboxMessage message) => _messages[message.Id] = message;

    public void BeginTransaction()
        => _snapshot = _messages.ToDictionary(
            entry => entry.Key,
            entry => Clone(entry.Value));

    public void CommitTransaction() => _snapshot = null;

    public void RollbackTransaction()
    {
        if (_snapshot is null)
        {
            return;
        }

        _messages.Clear();

        foreach (var entry in _snapshot)
        {
            _messages[entry.Key] = entry.Value;
        }

        _snapshot = null;
    }

    public Task<UnitResult<Failure>> AddAsync(
        EmailOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        AddCallCount++;
        AddInTransaction |= IsInTransaction?.Invoke() ?? false;

        if (AddBehavior is { } behavior)
        {
            return Task.FromResult(behavior(message));
        }

        // Аналог ux_email_outbox_deduplication_key: одно письмо — одна строка.
        if (_messages.Values.Any(existing =>
                existing.DeduplicationKey == message.DeduplicationKey))
        {
            return Task.FromResult(UnitResult.Failure(new Failure(Error.Conflict(
                "emailOutbox.add.uniqueViolation",
                "An outbox row with the same deduplication key already exists."))));
        }

        _messages[message.Id] = Clone(message);

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<Result<IReadOnlyList<EmailOutboxMessage>, Failure>> GetReadyForDispatchAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            return Task.FromResult<Result<IReadOnlyList<EmailOutboxMessage>, Failure>>(
                new Failure(Error.Validation(
                    "emailOutbox.dispatch.batchSize",
                    "Batch size must be at least one.",
                    nameof(batchSize))));
        }

        IReadOnlyList<EmailOutboxMessage> ready = _messages.Values
            .Where(message => message.IsReady(now))
            .OrderBy(message => message.NextAttemptAt)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<EmailOutboxMessage>, Failure>(ready));
    }

    public Task<UnitResult<Failure>> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(UnitResult.Success<Failure>());

    public Task<int> DeleteProcessedAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now - retention;

        var staleIds = _messages.Values
            .Where(message =>
                (message.SentAt is not null && message.SentAt < cutoff)
                || (message.FailedAt is not null && message.FailedAt < cutoff))
            .Select(message => message.Id)
            .ToList();

        foreach (var id in staleIds)
        {
            _messages.Remove(id);
        }

        return Task.FromResult(staleIds.Count);
    }

    /// <summary>
    /// Копия строки с тем же <c>Id</c>: «таблица» и снимок не должны делить состояние.
    /// </summary>
    private static EmailOutboxMessage Clone(EmailOutboxMessage message)
    {
        var memberwiseClone = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (EmailOutboxMessage)memberwiseClone.Invoke(message, null)!;
    }
}
