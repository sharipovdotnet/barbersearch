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
/// Маппинг SQLSTATE <c>55P03</c> (<c>lock_not_available</c>) — без подключения к базе (R-18).
/// </summary>
/// <remarks>
/// <see cref="ThrowingDbContext"/> воспроизводит <see cref="PostgresException"/>, который
/// Npgsql бросает после истечения <c>SET LOCAL lock_timeout</c>. Такой ответ обязан стать
/// <c>Error.Conflict</c> с кодом <c>booking.add.lockTimeout</c> (409), а не 500.
/// </remarks>
public class RepositoryLockTimeoutTests
{
    [Fact]
    public async Task SaveChanges_WhenLockTimeout_ReturnsConflict()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test")
            .Options;
        await using var context = new ThrowingDbContext(options);
        var repository = new TestRepository(context);

        var result = await repository.SaveAsync(CancellationToken.None);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("booking.add.lockTimeout", error.Code);
        Assert.DoesNotContain("pg_advisory", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingDbContext : ApplicationDbContext
    {
        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new PostgresException(
                "canceling statement due to lock timeout",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.LockNotAvailable);
    }

    private sealed class TestRepository : RepositoryBase<Booking>
    {
        public TestRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public Task<UnitResult<Failure>> SaveAsync(CancellationToken cancellationToken)
            => SaveChangesAsync("booking.add", cancellationToken);
    }
}
