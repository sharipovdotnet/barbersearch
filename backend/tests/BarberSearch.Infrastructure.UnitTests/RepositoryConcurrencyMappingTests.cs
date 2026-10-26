using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг проигранной оптимистической блокировки в 409-код (R-05).
/// </summary>
/// <remarks>
/// <see cref="ThrowingDbContext"/> воспроизводит то, что EF Core бросает после
/// <c>UPDATE ... WHERE xmin = @original</c>, не открывая соединение с PostgreSQL.
/// </remarks>
public class RepositoryConcurrencyMappingTests
{
    [Fact]
    public async Task SaveChanges_WhenConcurrencyTokenChanged_ReturnsConflict()
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
        Assert.Equal("booking.update.concurrencyConflict", error.Code);
        Assert.Contains("modified or deleted by another request", error.Message, StringComparison.Ordinal);
    }

    private sealed class ThrowingDbContext : ApplicationDbContext
    {
        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), but actually affected 0 row(s).");
    }

    private sealed class TestRepository : RepositoryBase<Booking>
    {
        public TestRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public Task<UnitResult<Failure>> SaveAsync(CancellationToken cancellationToken)
            => SaveChangesAsync(ApiErrorCodes.Booking.UpdatePrefix, cancellationToken);
    }
}
