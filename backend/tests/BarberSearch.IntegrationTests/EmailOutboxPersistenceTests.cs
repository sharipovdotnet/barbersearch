using BarberSearch.Application.Emails;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Схема <c>email_outbox</c> и поведение репозитория на реальной PostgreSQL: миграция создаёт
/// ожидаемые колонки, индексы и ограничения, ключ дедупликации не пускает дубль, выборка
/// диспетчера берёт только созревшие строки, а уборка удаляет только обработанные.
/// </summary>
/// <remarks>
/// Отдельно проверяется, что <c>FOR UPDATE SKIP LOCKED</c> действительно транслируется:
/// запрос идёт сырым SQL через EF Core, и ошибка трансляции проявилась бы здесь, а не
/// в проде.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class EmailOutboxPersistenceTests
{
    private const string DeduplicationIndex = "ux_email_outbox_deduplication_key";
    private const string DispatchIndex = "ix_email_outbox_dispatch";

    private readonly PostgresFixture _database;

    public EmailOutboxPersistenceTests(PostgresFixture database)
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
              AND table_name = 'email_outbox'
            ORDER BY ordinal_position
            """);

        Assert.Equal(
            [
                "id", "deduplication_key", "to_email", "subject", "html_body", "text_body",
                "next_attempt_at", "attempts", "sent_at", "failed_at", "last_error",
                "created_at", "updated_at"
            ],
            columns);

        var indexes = await ReadPairsAsync(
            connection,
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'email_outbox'
            """);

        var deduplication = Assert.Contains(DeduplicationIndex, indexes);
        Assert.Contains("CREATE UNIQUE INDEX", deduplication, StringComparison.Ordinal);
        Assert.Contains("(deduplication_key)", deduplication, StringComparison.Ordinal);

        var dispatch = Assert.Contains(DispatchIndex, indexes);
        Assert.Contains(
            "(sent_at, failed_at, next_attempt_at)",
            dispatch,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE", dispatch, StringComparison.Ordinal);

        var constraints = await ReadPairsAsync(
            connection,
            """
            SELECT conname, pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = 'public.email_outbox'::regclass
            """);

        Assert.Contains(
            "attempts >= 0",
            Assert.Contains("ck_email_outbox_attempts", constraints),
            StringComparison.Ordinal);

        // PostgreSQL нормализует выражение, добавляя скобки вокруг операндов.
        var state = Assert.Contains("ck_email_outbox_state", constraints);
        Assert.Contains("NOT", state, StringComparison.Ordinal);
        Assert.Contains("sent_at IS NOT NULL", state, StringComparison.Ordinal);
        Assert.Contains("failed_at IS NOT NULL", state, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task Duplicate_deduplication_key_is_rejected_as_conflict()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var repository = new EmailOutboxRepository(context);

        var first = RequireMessage("token-1", now);
        Assert.True((await repository.AddAsync(first, CancellationToken.None)).IsSuccess);

        var second = RequireMessage("token-1", now);
        var rejected = await repository.AddAsync(second, CancellationToken.None);

        Assert.True(rejected.IsFailure);
        var error = Assert.Single(rejected.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("emailOutbox.add.uniqueViolation", error.Code);

        Assert.Equal(1, await context.EmailOutbox.CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task GetReadyForDispatch_returns_only_due_rows_in_batch_order()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;

        var dueEarlier = RequireMessage("due-earlier", now.AddMinutes(-10));
        var dueLater = RequireMessage("due-later", now.AddMinutes(-5));
        var future = RequireMessage("future", now.AddMinutes(5));

        context.EmailOutbox.AddRange(dueEarlier, dueLater, future);
        await context.SaveChangesAsync();

        var repository = new EmailOutboxRepository(context);

        // FOR UPDATE SKIP LOCKED живёт до конца транзакции — как в диспетчере.
        await using var transaction = await context.Database.BeginTransactionAsync();

        var ready = await repository.GetReadyForDispatchAsync(now, batchSize: 10, CancellationToken.None);
        Assert.True(ready.IsSuccess);
        Assert.Equal(
            [dueEarlier.Id, dueLater.Id],
            ready.Value.Select(message => message.Id).ToList());

        await transaction.RollbackAsync();

        // Лимит пачки соблюдается.
        var limited = await repository.GetReadyForDispatchAsync(now, batchSize: 1, CancellationToken.None);
        Assert.True(limited.IsSuccess);
        Assert.Equal(dueEarlier.Id, Assert.Single(limited.Value).Id);
    }

    [Fact(Timeout = 120_000)]
    public async Task DeleteProcessed_deletes_only_rows_older_than_retention()
    {
        await _database.ResetAsync();

        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;

        var sentLongAgo = RequireMessage("sent-long-ago", now.AddDays(-10));
        Assert.True(sentLongAgo.MarkSent(now.AddDays(-9)).IsSuccess);

        var failedLongAgo = RequireMessage("failed-long-ago", now.AddDays(-10));
        Assert.True(failedLongAgo.RecordFailure(
            "smtp.unavailable",
            now.AddDays(-9),
            now.AddDays(-9),
            maxAttempts: 1).IsSuccess);

        var sentRecently = RequireMessage("sent-recently", now.AddDays(-10));
        Assert.True(sentRecently.MarkSent(now.AddDays(-1)).IsSuccess);

        var pending = RequireMessage("pending", now.AddDays(-10));

        context.EmailOutbox.AddRange(sentLongAgo, failedLongAgo, sentRecently, pending);
        await context.SaveChangesAsync();

        var repository = new EmailOutboxRepository(context);
        var deleted = await repository.DeleteProcessedAsync(
            now,
            TimeSpan.FromDays(7),
            CancellationToken.None);

        Assert.Equal(2, deleted);

        var remaining = await context.EmailOutbox
            .AsNoTracking()
            .Select(message => message.DeduplicationKey)
            .ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains("sent-recently", remaining);
        Assert.Contains("pending", remaining);
        Assert.DoesNotContain("sent-long-ago", remaining);
        Assert.DoesNotContain("failed-long-ago", remaining);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_database.ConnectionString)
            .Options);

    private static EmailOutboxMessage RequireMessage(string deduplicationKey, DateTimeOffset now)
    {
        var created = EmailOutboxMessage.Create(
            deduplicationKey,
            "client@example.com",
            "Сброс пароля — BarberSearch",
            "<p><a href=\"http://localhost:5173/reset-password?token=abc_DEF-123\">Задать пароль</a></p>",
            "Задать новый пароль: http://localhost:5173/reset-password?token=abc_DEF-123",
            now);

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
