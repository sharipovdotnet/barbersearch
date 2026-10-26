using System.Data.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// <see cref="UnitOfWork"/> очищает change tracker после отката (R-30), но не после успешного
/// коммита.
/// </summary>
/// <remarks>
/// <para>
/// Транзакция подменена через
/// <c>ReplaceService&lt;IDbContextTransactionManager, FakeTransactionManager&gt;</c>: соединение
/// с базой не открывается, фейк считает <c>Commit</c>/<c>Rollback</c>. Незавершённая транзакция
/// откатывается при dispose — так же, как реальная транзакция PostgreSQL при выходе из
/// <c>await using</c> без явного <c>RollbackAsync</c>.
/// </para>
/// <para>
/// Проверяются три пути: <c>Failure</c> (один явный откат), исключение из операции (один
/// неявный откат при dispose, исключение уходит наружу) и успех (<c>Commit</c>, трекер не
/// очищен).
/// </para>
/// </remarks>
public class UnitOfWorkTrackerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOperationReturnsFailure_RollsBackOnceAndClearsTracker()
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var booking = CreateBooking();

        context.Bookings.Add(booking);
        Assert.Single(context.ChangeTracker.Entries());

        var result = await unitOfWork.ExecuteAsync(FailedOperation);

        Assert.True(result.IsFailure);
        var manager = GetManager(context);
        Assert.Equal(1, manager.RollbackCount);
        Assert.Equal(0, manager.CommitCount);
        Assert.Empty(context.ChangeTracker.Entries());

        // После очистки повторный SaveChanges не пишет ничего: в трекере нет Added/Modified,
        // соединение с несуществующей базой даже не открывается.
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_RollsBackOnceClearsTrackerAndRethrows()
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var booking = CreateBooking();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.ExecuteAsync(_ => ThrowingOperation(context, booking)));

        Assert.Equal("Operation failed.", exception.Message);
        var manager = GetManager(context);
        Assert.Equal(1, manager.RollbackCount);
        Assert.Equal(0, manager.CommitCount);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationSucceeds_CommitsAndKeepsTracker()
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var booking = CreateBooking();

        var result = await unitOfWork.ExecuteAsync(_ =>
        {
            context.Bookings.Add(booking);

            return Task.FromResult(UnitResult.Success<Failure>());
        });

        Assert.True(result.IsSuccess);
        var manager = GetManager(context);
        Assert.Equal(1, manager.CommitCount);
        Assert.Equal(0, manager.RollbackCount);

        // Успешный коммит трекер не очищает: обработчик может продолжить работу с сущностями.
        var entry = Assert.Single(context.ChangeTracker.Entries());
        Assert.Same(booking, entry.Entity);
        Assert.Equal(EntityState.Added, entry.State);
    }

    private static Task<UnitResult<Failure>> FailedOperation(CancellationToken cancellationToken)
        => Task.FromResult(UnitResult.Failure<Failure>(
            new Failure(Error.Validation("unitOfWork.failed", "Operation failed."))));

    private static Task<UnitResult<Failure>> ThrowingOperation(
        ApplicationDbContext context,
        Booking booking)
    {
        context.Bookings.Add(booking);

        throw new InvalidOperationException("Operation failed.");
    }

    private static FakeTransactionManager GetManager(ApplicationDbContext context)
        => Assert.IsType<FakeTransactionManager>(
            context.GetService<IDbContextTransactionManager>());

    private static ApplicationDbContext CreateContext()
        => new(CreateOptions());

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test")
            .ReplaceService<IDbContextTransactionManager, FakeTransactionManager>()
            .Options;

    private static Booking CreateBooking()
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            1500m,
            $"key-{Guid.NewGuid()}").Value;
    }
}

/// <summary>
/// Менеджер транзакций без базы: считает коммиты и откаты, отдаёт фейковую транзакцию.
/// </summary>
internal sealed class FakeTransactionManager : IDbContextTransactionManager
{
    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public IDbContextTransaction? CurrentTransaction { get; private set; }

    public IDbContextTransaction BeginTransaction()
    {
        CurrentTransaction = new FakeDbContextTransaction(this);

        return CurrentTransaction;
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(BeginTransaction());

    public void CommitTransaction() => CommitCount++;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitTransaction();

        return Task.CompletedTask;
    }

    public void RollbackTransaction() => RollbackCount++;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        RollbackTransaction();

        return Task.CompletedTask;
    }

    public void EnlistTransaction(DbTransaction? transaction)
        => throw new NotSupportedException();

    public void ResetState() => CurrentTransaction = null;

    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        return Task.CompletedTask;
    }

    internal void RegisterCommit()
    {
        CommitCount++;
        CurrentTransaction = null;
    }

    internal void RegisterRollback()
    {
        RollbackCount++;
        CurrentTransaction = null;
    }
}

/// <summary>
/// Фейковая транзакция: считает завершение и имитирует откат при dispose незавершённой
/// транзакции (поведение реальной транзакции при выходе из <c>await using</c>).
/// </summary>
internal sealed class FakeDbContextTransaction : IDbContextTransaction
{
    private readonly FakeTransactionManager _manager;
    private bool _completed;

    public FakeDbContextTransaction(FakeTransactionManager manager)
    {
        _manager = manager;
    }

    public Guid TransactionId { get; } = Guid.NewGuid();

    public bool SupportsSavepoints => false;

    public void Commit()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _manager.RegisterCommit();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();

        return Task.CompletedTask;
    }

    public void Rollback()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _manager.RegisterRollback();
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();

        return Task.CompletedTask;
    }

    public void CreateSavepoint(string name) => throw new NotSupportedException();

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void RollbackToSavepoint(string name) => throw new NotSupportedException();

    public Task RollbackToSavepointAsync(
        string name,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void ReleaseSavepoint(string name) => throw new NotSupportedException();

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public void Dispose() => Rollback();

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
