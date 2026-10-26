using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Запись брони без собственной транзакции (R-15): <c>AddAsync</c> сохраняет изменения
/// в транзакции <c>IUnitOfWork</c> и снимает неудачную сущность с change tracker.
/// </summary>
/// <remarks>
/// Подключение указывает на закрытый порт, поэтому попытка открыть транзакцию упала бы
/// <see cref="NpgsqlException"/> ещё до <c>SaveChanges</c>. Успешный перехват ошибки
/// сохранения доказывает, что репозиторий не начинал транзакцию сам.
/// </remarks>
public class BookingRepositoryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public async Task AddAsync_WhenSaveFails_ReturnsFailureAndDetachesBooking()
    {
        var exception = new PostgresException(
            "duplicate key value violates unique constraint \"ux_bookings_user_id_idempotency_key\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: "ux_bookings_user_id_idempotency_key");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new BookingRepository(context);
        var booking = CreateBooking();

        var result = await repository.AddAsync(booking, CancellationToken.None);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("booking.add.uniqueViolation", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(booking).State);
    }

    [Fact]
    public async Task AddAsync_WhenSaveThrows_DetachesBookingAndRethrows()
    {
        var exception = new InvalidOperationException("save failed");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new BookingRepository(context);
        var booking = CreateBooking();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(booking, CancellationToken.None));

        Assert.Same(exception, thrown);
        Assert.Equal(EntityState.Detached, context.Entry(booking).State);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
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
}
