using BarberSearch.Domain.EmailOutbox;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Emails;

/// <summary>
/// Доступ к таблице исходящих писем <c>email_outbox</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AddAsync"/> вызывается только из транзакции обработчика запроса письма
/// (<c>IUnitOfWork</c>): строка outbox и новый токен либо фиксируются вместе, либо вместе
/// откатываются.
/// </para>
/// <para>
/// <see cref="GetReadyForDispatchAsync"/> читает пачку строк с блокировкой
/// <c>FOR UPDATE SKIP LOCKED</c> и поэтому обязан вызываться внутри транзакции
/// <c>IUnitOfWork</c>: блокировка живёт до коммита, и второй инстанс приложения пропускает
/// уже занятые строки, а не отправляет их повторно. Пачка остаётся заблокированной, пока
/// диспетчер шлёт письма; аварийное завершение снимает блокировку, и строки вернутся
/// в следующую пачку (at-least-once).
/// </para>
/// <para>
/// Ошибки сохранения — <c>Error.Conflict</c> / <c>Error.Validation</c> с кодом
/// <c>emailOutbox.*</c>; нарушение уникальности <see cref="EmailOutboxMessage.DeduplicationKey"/>
/// приходит как <c>409 emailOutbox.add.uniqueViolation</c>.
/// </para>
/// </remarks>
public interface IEmailOutboxRepository
{
    /// <summary>
    /// Добавляет строку в outbox.
    /// </summary>
    /// <param name="message">Готовое письмо.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> AddAsync(
        EmailOutboxMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Забирает пачку готовых к отправке строк и блокирует их до конца транзакции.
    /// </summary>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    /// <param name="batchSize">Размер пачки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<EmailOutboxMessage>, Failure>> GetReadyForDispatchAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения заблокированной пачки (<c>sent_at</c>, <c>attempts</c>,
    /// <c>next_attempt_at</c>, <c>failed_at</c>).
    /// </summary>
    Task<UnitResult<Failure>> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет обработанные строки старше <c>now - retention</c> и возвращает число удалённых.
    /// </summary>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    /// <param name="retention">Срок хранения отправленных и окончательно проваленных строк.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<int> DeleteProcessedAsync(
        DateTimeOffset now,
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}
