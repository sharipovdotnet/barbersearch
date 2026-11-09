using BarberSearch.Domain.PasswordResets;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Перевод ограничений PostgreSQL в ошибки для токенов сброса пароля: частичный уникальный
/// индекс активных строк — внутренний <c>409 passwordResetToken.issue.conflict</c>, коллизия
/// хеша — <c>500 internal.database</c>; проигранная гонка обновления — <c>409</c>.
/// </summary>
/// <remarks>
/// Подключение указывает на закрытый порт, поэтому настоящий SQL не выполняется: контекст
/// подменяет <c>SaveChangesAsync</c> готовым исключением. Это проверяет маппинг, а не базу;
/// поведение реального индекса проверяет интеграционный тест. Предикат уборки проверяется
/// напрямую: он же используется для снятия удалённых строк с change tracker.
/// </remarks>
public class PasswordResetTokenRepositoryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_WhenSecondActiveRowInserted_ReturnsIssueConflict()
    {
        var exception = UniqueViolation(PasswordResetTokenRepository.ACTIVE_INDEX_NAME);
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new PasswordResetTokenRepository(context);
        var token = CreateToken();

        var result = await repository.AddAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("passwordResetToken.issue.conflict", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(token).State);
    }

    [Fact]
    public async Task AddAsync_WhenTokenHashCollides_ReturnsInternalFailure()
    {
        var exception = UniqueViolation(PasswordResetTokenRepository.HASH_INDEX_NAME);
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new PasswordResetTokenRepository(context);
        var token = CreateToken();

        var result = await repository.AddAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.FAILURE, error.Type);
        Assert.Equal("internal.database", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(token).State);
    }

    [Fact]
    public async Task AddAsync_WhenOtherUniqueConstraintFails_UsesGenericPersistenceCode()
    {
        var exception = UniqueViolation("ux_some_other_index");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new PasswordResetTokenRepository(context);
        var token = CreateToken();

        var result = await repository.AddAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("auth.passwordReset.uniqueViolation", error.Code);
    }

    [Fact]
    public async Task AddAsync_WhenSaveThrowsUnexpected_PropagatesException()
    {
        var exception = new InvalidOperationException("save failed");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new PasswordResetTokenRepository(context);
        var token = CreateToken();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(token, CancellationToken.None));

        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyLost_ReturnsConcurrencyConflict()
    {
        var exception = new DbUpdateConcurrencyException("row was modified by another request");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new PasswordResetTokenRepository(context);
        var token = CreateToken();

        var result = await repository.UpdateAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("passwordResetToken.update.concurrencyConflict", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(token).State);
    }

    [Fact]
    public async Task GetByHashAsync_WithEmptyHash_ReturnsValidationFailure()
    {
        await using var context = new ThrowingDbContext(
            CreateOptions(),
            new InvalidOperationException("must not be called"));
        var repository = new PasswordResetTokenRepository(context);

        var result = await repository.GetByHashAsync(" ", CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(PasswordResetTokenRepository.INVALID_HASH_CODE, error.Code);
    }

    [Fact]
    public void IsExpired_KeepsRowsYoungerThanRetention()
    {
        var cutoff = Now.AddDays(-30);

        // Активная и недавно использованная строки остаются.
        Assert.False(PasswordResetTokenRepository.IsExpired(
            CreateToken(expiresAt: Now.AddMinutes(30)),
            cutoff));

        var recent = CreateToken(expiresAt: Now.AddDays(1));
        Assert.True(recent.Consume(cutoff.AddDays(1)).IsSuccess);
        Assert.False(PasswordResetTokenRepository.IsExpired(recent, cutoff));
    }

    [Fact]
    public void IsExpired_DeletesOnlyStrictlyOlderRows()
    {
        var cutoff = Now.AddDays(-30);

        Assert.True(PasswordResetTokenRepository.IsExpired(
            CreateToken(expiresAt: cutoff.AddTicks(-1)),
            cutoff));
        Assert.False(PasswordResetTokenRepository.IsExpired(
            CreateToken(issuedAt: cutoff.AddDays(-1), expiresAt: cutoff),
            cutoff));

        var consumedOld = CreateToken(expiresAt: Now.AddDays(1));
        Assert.True(consumedOld.Consume(cutoff.AddTicks(-1)).IsSuccess);
        Assert.True(PasswordResetTokenRepository.IsExpired(consumedOld, cutoff));

        var consumedAtBoundary = CreateToken(expiresAt: Now.AddDays(1));
        Assert.True(consumedAtBoundary.Consume(cutoff).IsSuccess);
        Assert.False(PasswordResetTokenRepository.IsExpired(consumedAtBoundary, cutoff));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

    private static PostgresException UniqueViolation(string constraintName)
        => new(
            $"duplicate key value violates unique constraint \"{constraintName}\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName);

    private static PasswordResetToken CreateToken(
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var issued = issuedAt ?? Now.AddDays(-31);

        return PasswordResetToken.Create(
            Guid.CreateVersion7(),
            new string('a', PasswordResetToken.HASH_LENGTH),
            issued,
            expiresAt ?? Now.AddDays(-30),
            issued).Value;
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
