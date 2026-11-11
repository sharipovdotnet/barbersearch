using BarberSearch.Domain.EmailChanges;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Перевод ограничений PostgreSQL в публичные ошибки для токенов смены email: частичный
/// уникальный индекс активных строк — <c>409 auth.emailChange.cooldown</c>, коллизия хеша —
/// <c>500 internal.database</c>, проигранная гонка обновления — <c>409</c>.
/// </summary>
/// <remarks>
/// Подключение указывает на закрытый порт, поэтому настоящий SQL не выполняется: контекст
/// подменяет <c>SaveChangesAsync</c> готовым исключением. Это проверяет маппинг, а не базу;
/// поведение реальных индексов проверяет интеграционный тест.
/// </remarks>
public class EmailChangeTokenRepositoryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public async Task AddAsync_WhenSecondActiveRowInserted_ReturnsCooldownConflict()
    {
        var exception = UniqueViolation(EmailChangeTokenRepository.ACTIVE_INDEX_NAME);
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new EmailChangeTokenRepository(context);
        var token = CreateToken();

        var result = await repository.AddAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("auth.emailChange.cooldown", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(token).State);
    }

    [Fact]
    public async Task AddAsync_WhenTokenHashCollides_ReturnsInternalFailure()
    {
        var exception = UniqueViolation(EmailChangeTokenRepository.HASH_INDEX_NAME);
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new EmailChangeTokenRepository(context);
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
        var repository = new EmailChangeTokenRepository(context);
        var token = CreateToken();

        var result = await repository.AddAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("auth.emailChange.uniqueViolation", error.Code);
    }

    [Fact]
    public async Task AddAsync_WhenSaveThrowsUnexpected_PropagatesException()
    {
        var exception = new InvalidOperationException("save failed");
        await using var context = new ThrowingDbContext(CreateOptions(), exception);
        var repository = new EmailChangeTokenRepository(context);
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
        var repository = new EmailChangeTokenRepository(context);
        var token = CreateToken();

        var result = await repository.UpdateAsync(token, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("emailChangeToken.update.concurrencyConflict", error.Code);
        Assert.Equal(EntityState.Detached, context.Entry(token).State);
    }

    [Fact]
    public async Task GetByHashAsync_WithEmptyHash_ReturnsValidationFailure()
    {
        await using var context = new ThrowingDbContext(
            CreateOptions(),
            new InvalidOperationException("must not be called"));
        var repository = new EmailChangeTokenRepository(context);

        var result = await repository.GetByHashAsync(" ", CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(EmailChangeTokenRepository.INVALID_HASH_CODE, error.Code);
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

    private static EmailChangeToken CreateToken()
    {
        var now = DateTimeOffset.UtcNow;

        return EmailChangeToken.Create(
            Guid.CreateVersion7(),
            "new@example.com",
            new string('a', EmailChangeToken.HASH_LENGTH),
            now,
            now.AddHours(24),
            now).Value;
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
