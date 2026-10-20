using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql;

/// <summary>
/// Транзакция над <see cref="ApplicationDbContext"/>.
/// </summary>
/// <remarks>
/// Один scoped контекст на запрос: все репозитории, вызванные внутри операции, пишут в ту же
/// транзакцию. Коммит выполняется только при успешном результате; <c>Failure</c> и исключения
/// приводят к откату. Исключение не превращается в успех и не глотается — оно уходит наверх,
/// после того как <c>await using</c> откатил транзакцию.
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
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var result = await operation(cancellationToken);

        if (isFailure(result))
        {
            await transaction.RollbackAsync(cancellationToken);

            return result;
        }

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
