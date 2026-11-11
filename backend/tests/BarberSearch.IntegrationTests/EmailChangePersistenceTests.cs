using BarberSearch.Application.EmailChanges;
using BarberSearch.Domain.EmailChanges;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Схема <c>email_change_tokens</c> и поведение репозитория на реальной PostgreSQL: миграция
/// создаёт ожидаемые ограничения, частичный индекс не пускает вторую активную строку,
/// уборка удаляет только старое.
/// </summary>
/// <remarks>
/// Это инфраструктурные проверки, которым нужна настоящая база: unit-проект
/// <c>BarberSearch.Infrastructure.UnitTests</c> принципиально не подключается к PostgreSQL
/// и проверяет только модель. Здесь используется <see cref="PostgresFixture"/> — та же база
/// прогона, что и у остальных интеграционных тестов.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class EmailChangePersistenceTests
{
    private readonly PostgresFixture _database;

    public EmailChangePersistenceTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Migration_creates_table_with_expected_columns_indexes_and_constraints()
    {
        await _database.ResetAsync();

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        var columns = await ReadLinesAsync(
            connection,
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'email_change_tokens'
            ORDER BY ordinal_position
            """);

        Assert.Equal(
            [
                "id", "user_id", "new_email", "token_hash", "issued_at", "expires_at",
                "consumed_at", "revoked_at", "created_at", "updated_at"
            ],
            columns);

        var newEmailLength = await ReadScalarAsync(
            connection,
            """
            SELECT character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'email_change_tokens'
              AND column_name = 'new_email'
            """);

        Assert.Equal(254, Assert.IsType<int>(newEmailLength));

        var indexes = await ReadPairsAsync(
            connection,
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'email_change_tokens'
            """);

        var active = Assert.Contains("ux_email_change_tokens_active", indexes);
        Assert.Contains("CREATE UNIQUE INDEX", active, StringComparison.Ordinal);
        Assert.Contains("(user_id)", active, StringComparison.Ordinal);
        Assert.Contains("consumed_at IS NULL", active, StringComparison.Ordinal);
        Assert.Contains("revoked_at IS NULL", active, StringComparison.Ordinal);

        var hash = Assert.Contains("ux_email_change_tokens_hash", indexes);
        Assert.Contains("CREATE UNIQUE INDEX", hash, StringComparison.Ordinal);
        Assert.Contains("(token_hash)", hash, StringComparison.Ordinal);

        var expires = Assert.Contains("ix_email_change_tokens_expires_at", indexes);
        Assert.Contains("(expires_at)", expires, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE", expires, StringComparison.Ordinal);

        var constraints = await ReadPairsAsync(
            connection,
            """
            SELECT conname, pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = 'public.email_change_tokens'::regclass
            """);

        Assert.Contains(
            "char_length(token_hash) = 64",
            Assert.Contains("ck_email_change_tokens_token_hash", constraints),
            StringComparison.Ordinal);

        // PostgreSQL нормализует запись предиката, поэтому проверяются его части.
        var newEmailCheck = Assert.Contains("ck_email_change_tokens_new_email", constraints);
        Assert.Contains("new_email", newEmailCheck, StringComparison.Ordinal);
        Assert.Contains("'@'", newEmailCheck, StringComparison.Ordinal);
        Assert.Contains("> 1", newEmailCheck, StringComparison.Ordinal);

        var newEmailLengthCheck = Assert.Contains(
            "ck_email_change_tokens_new_email_length",
            constraints);
        Assert.Contains("char_length", newEmailLengthCheck, StringComparison.Ordinal);
        Assert.Contains("new_email", newEmailLengthCheck, StringComparison.Ordinal);
        Assert.Contains("254", newEmailLengthCheck, StringComparison.Ordinal);

        var foreignKey = Assert.Contains("fk_email_change_tokens_users_user_id", constraints);
        Assert.Contains("ON DELETE CASCADE", foreignKey, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task Second_active_token_for_one_user_is_rejected_as_cooldown()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var user = await SeedUserAsync(context);
        var repository = new EmailChangeTokenRepository(context);

        var first = RequireToken(user.Id, now, TimeSpan.FromHours(24));
        Assert.True((await repository.AddAsync(first, CancellationToken.None)).IsSuccess);

        var second = RequireToken(user.Id, now, TimeSpan.FromHours(24));
        var rejected = await repository.AddAsync(second, CancellationToken.None);

        Assert.True(rejected.IsFailure);
        var error = Assert.Single(rejected.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("auth.emailChange.cooldown", error.Code);

        Assert.Equal(1, await context.EmailChangeTokens.CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Duplicate_hash_for_another_user_surfaces_as_internal_failure()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var firstUser = await SeedUserAsync(context);
        var secondUser = await SeedUserAsync(context);
        var repository = new EmailChangeTokenRepository(context);

        var hash = EmailChangeTokenGenerator.ComputeHash(
            EmailChangeTokenGenerator.Generate());

        var first = RequireToken(firstUser.Id, now, TimeSpan.FromHours(24), hash);
        Assert.True((await repository.AddAsync(first, CancellationToken.None)).IsSuccess);

        var second = RequireToken(secondUser.Id, now, TimeSpan.FromHours(24), hash);
        var rejected = await repository.AddAsync(second, CancellationToken.None);

        Assert.True(rejected.IsFailure);
        var error = Assert.Single(rejected.Error.Errors);
        Assert.Equal(ErrorType.FAILURE, error.Type);
        Assert.Equal("internal.database", error.Code);

        Assert.Equal(1, await context.EmailChangeTokens.CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task DeleteExpired_deletes_only_rows_older_than_retention()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var user = await SeedUserAsync(context);

        // Истёк 40 дней назад; отозван, чтобы не занимать активный индекс.
        var oldExpired = RequireToken(user.Id, now.AddDays(-41), TimeSpan.FromDays(1));
        Assert.True(oldExpired.Revoke(now.AddDays(-40)).IsSuccess);

        // Использован 35 дней назад, срок ещё не истёк: удаляется по consumed_at.
        var oldConsumed = RequireToken(user.Id, now.AddDays(-40), TimeSpan.FromDays(60));
        Assert.True(oldConsumed.Consume(now.AddDays(-35)).IsSuccess);

        // Истёк 2 дня назад — ещё в окне хранения.
        var recent = RequireToken(user.Id, now.AddDays(-31), TimeSpan.FromDays(29));
        Assert.True(recent.Revoke(now.AddDays(-2)).IsSuccess);

        // Активный — не трогается.
        var active = RequireToken(user.Id, now, TimeSpan.FromHours(24));

        context.EmailChangeTokens.AddRange(oldExpired, oldConsumed, recent, active);
        await context.SaveChangesAsync();

        var repository = new EmailChangeTokenRepository(context);
        var deleted = await repository.DeleteExpiredAsync(
            now,
            EmailChangeLifetime.TOKEN_RETENTION,
            CancellationToken.None);

        Assert.Equal(2, deleted);

        var remaining = await context.EmailChangeTokens
            .AsNoTracking()
            .Select(token => token.TokenHash)
            .ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains(recent.TokenHash, remaining);
        Assert.Contains(active.TokenHash, remaining);
        Assert.DoesNotContain(oldExpired.TokenHash, remaining);
        Assert.DoesNotContain(oldConsumed.TokenHash, remaining);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_database.ConnectionString)
            .Options);

    private static async Task<User> SeedUserAsync(ApplicationDbContext context)
    {
        var created = User.Create(
            $"email-change-persistence-{Guid.NewGuid():N}@barbersearch.test",
            "hash",
            "Integration",
            "Test");

        Assert.True(created.IsSuccess);

        context.Users.Add(created.Value);
        await context.SaveChangesAsync();

        return created.Value;
    }

    private static EmailChangeToken RequireToken(
        Guid userId,
        DateTimeOffset issuedAt,
        TimeSpan lifetime,
        string? hash = null)
    {
        var created = EmailChangeToken.Create(
            userId,
            "new@example.com",
            hash ?? EmailChangeTokenGenerator.ComputeHash(
                EmailChangeTokenGenerator.Generate()),
            issuedAt,
            issuedAt + lifetime,
            issuedAt);

        Assert.True(created.IsSuccess);

        return created.Value;
    }

    private static async Task<IReadOnlyList<string>> ReadLinesAsync(
        NpgsqlConnection connection,
        string sql)
    {
        var values = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<object?> ReadScalarAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync();
    }

    private static async Task<Dictionary<string, string>> ReadPairsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }
}
