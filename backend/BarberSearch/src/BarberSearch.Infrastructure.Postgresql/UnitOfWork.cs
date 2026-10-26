using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql;

/// <summary>
/// Транзакция над <see cref="ApplicationDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Один scoped контекст на запрос: все репозитории, вызванные внутри операции, пишут в ту же
/// транзакцию. Коммит выполняется только при успешном результате; <c>Failure</c> и исключения
/// приводят к откату. Исключение не превращается в успех и не глотается — оно уходит наверх,
/// после того как <c>await using</c> откатил транзакцию.
/// </para>
/// <para>
/// После отката change tracker очищается: и при <c>Failure</c>, и при исключении из операции
/// или коммита. Иначе изменённые сущности остались бы отслеживаемыми, и повторный
/// <c>SaveChanges</c> в том же scope записал бы откатанные изменения в БД. При успешном коммите
/// трекер не очищается — обработчики могут рассчитывать на отслеживаемые сущности после
/// <c>IUnitOfWork.ExecuteAsync</c>.
/// </para>
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> ExecuteAsync(
        Func<CancellationToken, Task<UnitResult<Failure>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteCoreAsync(operation, result => result.IsFailure, cancellationToken);
    }

    /// <inheritdoc />
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
        var committed = false;

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var result = await operation(cancellationToken);

            if (isFailure(result))
            {
                await transaction.RollbackAsync(cancellationToken);

                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            return result;
        }
        finally
        {
            if (!committed)
            {
                // Failure или исключение: транзакция уже откатана явно либо откатится при
                // dispose (тогда сюда попадаем после выхода из using). Откатанные изменения
                // не должны пережить операцию — иначе повторный SaveChanges запишет их в БД.
                _context.ChangeTracker.Clear();
            }
        }
    }
}
