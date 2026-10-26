using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Неудачное сохранение не оставляет сущности в change tracker (R-17).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RepositoryBase{TEntity}"/> превращает ошибки ограничений
/// и проигранную оптимистическую блокировку в <see cref="Failure"/>. До R-17 неудачная
/// <c>Added</c>-сущность при этом оставалась отслеживаемой, поэтому повторный
/// <c>SaveChanges</c> в том же scope снова наткнулся бы на то же ограничение.
/// </para>
/// <para>
/// <see cref="ThrowingDbContext"/> бросает исключение до открытия соединения, поэтому тесты
/// не требуют PostgreSQL. Проверяется контракт «после <c>Failure</c> контекст пуст» для всех
/// трёх маппируемых путей: <see cref="DbUpdateException"/> с <c>23505</c>, отложенное
/// exclusion-ограничение в <c>COMMIT</c> и <see cref="DbUpdateConcurrencyException"/>.
/// </para>
/// </remarks>
public class RepositorySaveFailureTests
{
    [Fact]
    public async Task SaveChanges_WhenUniqueViolation_ReturnsFailureAndClearsTracker()
    {
        var postgres = new PostgresException(
            "duplicate key value violates unique constraint \"ux_bookings_user_id_idempotency_key\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: "ux_bookings_user_id_idempotency_key");
        await using var context = new ThrowingDbContext(
            CreateOptions(),
            new DbUpdateException("An error occurred while saving the entity changes.", postgres));
        var repository = new TestRepository(context);
        var added = CreateBooking();
        var tracked = CreateBooking();

        context.Bookings.Add(added);
        context.Attach(tracked);
        Assert.Equal(EntityState.Added, context.Entry(added).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);

        var result = await repository.SaveAsync(CancellationToken.None);

        AssertFailure(result, "booking.add.uniqueViolation");
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(EntityState.Detached, context.Entry(added).State);
        Assert.Equal(EntityState.Detached, context.Entry(tracked).State);
    }

    [Fact]
    public async Task SaveChanges_WhenDeferredConstraintViolation_ReturnsFailureAndClearsTracker()
    {
        var postgres = new PostgresException(
            "conflicting key value violates exclusion constraint \"ex_bookings_no_overlap\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.ExclusionViolation,
            constraintName: "ex_bookings_no_overlap");
        await using var context = new ThrowingDbContext(CreateOptions(), postgres);
        var repository = new TestRepository(context);
        var booking = CreateBooking();

        context.Bookings.Add(booking);

        var result = await repository.SaveAsync(CancellationToken.None);

        AssertFailure(result, "booking.add.overlap");
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SaveChanges_WhenConcurrencyConflict_ReturnsFailureAndClearsTracker()
    {
        var exception = new DbUpdateConcurrencyException(
            "The database operation was expected to affect 1 row(s), but actually affected 0 row(s).");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new TestRepository(context);
        var booking = CreateBooking();

        context.Bookings.Add(booking);

        var result = await repository.SaveAsync(CancellationToken.None);

        AssertFailure(result, "booking.add.concurrencyConflict");
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SaveChanges_WhenNonPostgresFailure_ReturnsInternalCodeAndClearsTracker()
    {
        await using var context = new ThrowingDbContext(
            CreateOptions(),
            new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new InvalidOperationException("provider failure")));
        var repository = new TestRepository(context);
        var booking = CreateBooking();

        context.Bookings.Add(booking);

        var result = await repository.SaveAsync(CancellationToken.None);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.FAILURE, error.Type);
        Assert.Equal(ApiErrorCodes.InternalDatabaseError, error.Code);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static void AssertFailure(UnitResult<Failure> result, string expectedCode)
    {
        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(expectedCode, error.Code);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test")
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

    private sealed class ThrowingDbContext : ApplicationDbContext
    {
        private readonly Exception _exception;

        public ThrowingDbContext(
            DbContextOptions<ApplicationDbContext> options,
            Exception exception)
            : base(options)
        {
            _exception = exception;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromException<int>(_exception);
    }

    private sealed class TestRepository : RepositoryBase<Booking>
    {
        public TestRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public Task<UnitResult<Failure>> SaveAsync(CancellationToken cancellationToken)
            => SaveChangesAsync(ApiErrorCodes.Booking.AddPrefix, cancellationToken);
    }
}
