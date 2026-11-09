using BarberSearch.Application.Emails;
using BarberSearch.Domain.EmailOutbox;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий исходящих писем <c>email_outbox</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GetReadyForDispatchAsync"/> использует <c>FOR UPDATE SKIP LOCKED</c>: строки
/// блокируются до конца транзакции <c>IUnitOfWork</c>, второй инстанс приложения их
/// пропускает. Выборка без блокировки здесь бессмысленна — два тика отправили бы одно письмо
/// дважды и обновили бы одну строку.
/// </para>
/// <para>
/// Аудит изменений (<c>AuditTrail</c>) сюда не передаётся: outbox — инфраструктурная таблица,
/// её изменения не являются бизнес-событиями и не должны попадать в <c>audit_log</c>.
/// </para>
/// </remarks>
public sealed class EmailOutboxRepository
    : RepositoryBase<EmailOutboxMessage>, IEmailOutboxRepository
{
    public EmailOutboxRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> AddAsync(
        EmailOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await Context.EmailOutbox.AddAsync(message, cancellationToken);

        // Нарушение ux_email_outbox_deduplication_key — 409 emailOutbox.add.uniqueViolation.
        return await SaveChangesAsync("emailOutbox.add", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EmailOutboxMessage>, Failure>> GetReadyForDispatchAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (now == default)
        {
            return new Failure(Error.Validation(
                "emailOutbox.dispatch.now",
                "Current time cannot be default.",
                nameof(now)));
        }

        if (batchSize < 1)
        {
            return new Failure(Error.Validation(
                "emailOutbox.dispatch.batchSize",
                "Batch size must be at least one.",
                nameof(batchSize)));
        }

        // Условие дублирует индекс ix_email_outbox_dispatch. ORDER BY + LIMIT фиксируют
        // порядок очереди, FOR UPDATE SKIP LOCKED отдаёт пачку одному тику: параллельный
        // диспетчер увидит занятые строки и не будет их ждать.
        var messages = await Context.EmailOutbox
            .FromSql($"""
                SELECT *
                FROM email_outbox
                WHERE sent_at IS NULL
                  AND failed_at IS NULL
                  AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        return messages;
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync("emailOutbox.save", cancellationToken);

    /// <inheritdoc />
    public async Task<int> DeleteProcessedAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now - retention;

        // Удаляются только обработанные строки старше ретенции: тело письма содержит
        // открытый токен, и хранить его дольше, чем нужно для доставки, нельзя.
        var deleted = await Context.EmailOutbox
            .Where(message =>
                (message.SentAt != null && message.SentAt < cutoff)
                || (message.FailedAt != null && message.FailedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete идёт мимо change tracker: отслеживаемые строки остались бы в памяти,
        // и следующий SaveChanges попытался бы их обновить.
        DetachTracked(message => IsProcessedBefore(message, cutoff));

        return deleted;
    }

    /// <summary>
    /// Предикат уборки: строка обработана раньше границы ретенции.
    /// </summary>
    /// <remarks>
    /// Отдельный метод, а не дважды записанное выражение: тот же предикат снимает удалённые
    /// строки с change tracker, и его граница проверяется unit-тестом без базы.
    /// </remarks>
    internal static bool IsProcessedBefore(EmailOutboxMessage message, DateTimeOffset cutoff)
        => (message.SentAt is not null && message.SentAt < cutoff)
           || (message.FailedAt is not null && message.FailedAt < cutoff);
}
