using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Участник фейковой транзакции: умеет снять снимок состояния и восстановить его при откате.
/// </summary>
public interface IFakeTransactionParticipant
{
    void BeginTransaction();

    void CommitTransaction();

    void RollbackTransaction();
}

/// <summary>
/// In-memory <see cref="IUnitOfWork"/> для тестов: снимает снимки фейковых репозиториев,
/// фиксирует их при успехе и восстанавливает при <c>Failure</c> или исключении.
/// </summary>
/// <remarks>
/// Настоящая транзакция откатывает строки в PostgreSQL; здесь её роль играют снимки,
/// потому что доменные объекты в фейках — те же экземпляры, которые мутирует обработчик.
/// </remarks>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly IReadOnlyList<IFakeTransactionParticipant> _participants;

    public FakeUnitOfWork(params IFakeTransactionParticipant[] participants)
    {
        ArgumentNullException.ThrowIfNull(participants);

        _participants = participants;
    }

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    /// <summary>
    /// Открыта ли фейковая транзакция. Тесты проверяют, что запись выполняется внутри
    /// <see cref="IUnitOfWork"/>, а не в собственной транзакции репозитория (R-15).
    /// </summary>
    public bool IsInTransaction { get; private set; }

    public Task<UnitResult<Failure>> ExecuteAsync(
        Func<CancellationToken, Task<UnitResult<Failure>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteCoreAsync(operation, result => result.IsFailure, cancellationToken);
    }

    public Task<Result<T, Failure>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T, Failure>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteCoreAsync(operation, result => result.IsFailure, cancellationToken);
    }

    private async Task<T> ExecuteCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> isFailure,
        CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            participant.BeginTransaction();
        }

        IsInTransaction = true;

        try
        {
            var result = await operation(cancellationToken);

            if (isFailure(result))
            {
                Rollback();
                return result;
            }

            Commit();
            return result;
        }
        catch
        {
            Rollback();
            throw;
        }
        finally
        {
            IsInTransaction = false;
        }
    }

    private void Commit()
    {
        foreach (var participant in _participants)
        {
            participant.CommitTransaction();
        }

        CommitCount++;
    }

    private void Rollback()
    {
        foreach (var participant in _participants)
        {
            participant.RollbackTransaction();
        }

        RollbackCount++;
    }
}
