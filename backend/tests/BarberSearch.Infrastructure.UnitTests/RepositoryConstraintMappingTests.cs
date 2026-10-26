using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет маппинг отложенного ограничения, которое PostgreSQL проверяет в <c>COMMIT</c>
/// (R-06), — без подключения к базе.
/// </summary>
/// <remarks>
/// <see cref="ThrowingDbContext"/> воспроизводит то, что Npgsql бросает при нарушении
/// <c>ex_barber_salons_primary_per_profile</c> в коммите неявной транзакции EF Core:
/// <see cref="PostgresException"/> напрямую, а не <see cref="DbUpdateException"/>.
/// </remarks>
public class RepositoryConstraintMappingTests
{
    [Fact]
    public async Task SaveChanges_WhenDeferredExclusionViolationAtCommit_ReturnsConflict()
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
        Assert.Equal("barberProfile.update.overlap", error.Code);
        Assert.Contains(
            "ex_barber_salons_primary_per_profile",
            error.Message,
            StringComparison.Ordinal);
    }

    private sealed class ThrowingDbContext : ApplicationDbContext
    {
        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new PostgresException(
                "conflicting key value violates exclusion constraint \"ex_barber_salons_primary_per_profile\"",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.ExclusionViolation,
                constraintName: "ex_barber_salons_primary_per_profile");
    }

    private sealed class TestRepository : RepositoryBase<BarberProfile>
    {
        public TestRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public Task<UnitResult<Failure>> SaveAsync(CancellationToken cancellationToken)
            => SaveChangesAsync(ApiErrorCodes.BarberProfile.UpdatePrefix, cancellationToken);
    }
}
