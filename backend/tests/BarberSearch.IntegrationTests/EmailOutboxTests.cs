using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.PasswordReset;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Transactional outbox на реальном хосте и реальной PostgreSQL: регистрация пишет строку
/// в той же транзакции, что и токен, диспетчер отправляет её тиком, сбой SMTP строку
/// не теряет, а два параллельных тика не отправляют письмо дважды.
/// </summary>
/// <remarks>
/// Транспорт подменён <see cref="CapturingEmailSender"/>: настоящий SMTP в тесте не участвует.
/// Строки читаются напрямую через <see cref="ApplicationDbContext"/> — тест проверяет
/// состояние таблицы, а не только письма.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class EmailOutboxTests
{
    private const string PublicUrl = "http://localhost:5173";

    private readonly PostgresFixture _database;

    public EmailOutboxTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 240_000)]
    public async Task Registration_writes_row_in_same_transaction_and_tick_delivers_it()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("outbox-registration");
        await app.RegisterAsync(email);

        // Регистрация только ставит письмо в очередь: транспорт ещё не вызывался.
        Assert.Empty(sender.Messages);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = Assert.Single(await db.EmailOutbox.AsNoTracking().ToListAsync());
            Assert.Equal(email, row.ToEmail);
            Assert.Null(row.SentAt);
            Assert.Null(row.FailedAt);
            Assert.Equal(0, row.Attempts);

            // timestamptz хранит микросекунды: сравниваем с точностью до миллисекунды.
            Assert.Equal(clock.UtcNow, row.NextAttemptAt, TimeSpan.FromMilliseconds(1));

            // Ключ дедупликации — id выданного токена: одно письмо на один токен.
            var token = Assert.Single(await db.EmailVerificationTokens.AsNoTracking().ToListAsync());
            Assert.Equal(token.Id.ToString("D"), row.DeduplicationKey);
        }

        var tick = await app.RunEmailDispatchAsync();

        Assert.Equal(1, tick.Sent);
        Assert.Equal(0, tick.Retried);
        Assert.Equal(0, tick.Failed);

        var message = Assert.Single(sender.Messages);
        Assert.Equal(email, message.To);
        Assert.Contains("/verify-email?token=", message.HtmlBody, StringComparison.Ordinal);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = Assert.Single(await db.EmailOutbox.AsNoTracking().ToListAsync());
            Assert.NotNull(row.SentAt);
            Assert.Null(row.FailedAt);
            Assert.Null(row.LastError);
        }

        // Повторный тик уже отправленную строку не трогает.
        var second = await app.RunEmailDispatchAsync();
        Assert.Equal(0, second.Sent);
        Assert.Single(sender.Messages);
    }

    [Fact(Timeout = 240_000)]
    public async Task Two_parallel_ticks_deliver_each_letter_exactly_once()
    {
        // Задержка держит блокировку пачки: второй тик стартует, пока первый ещё отправляет.
        var sender = new CapturingEmailSender { Delay = TimeSpan.FromMilliseconds(200) };
        var clock = new MutableTimeProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        await SeedOutboxAsync(app, clock.UtcNow, count: 2);

        var ticks = await Task.WhenAll(
            app.RunEmailDispatchAsync(),
            app.RunEmailDispatchAsync());

        // FOR UPDATE SKIP LOCKED отдаёт каждую строку ровно одному тику.
        Assert.Equal(2, sender.Messages.Count);
        Assert.Equal(2, ticks.Sum(tick => tick.Sent));
        Assert.Equal(0, ticks.Sum(tick => tick.Retried));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.EmailOutbox.AsNoTracking().ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.SentAt));
    }

    [Fact(Timeout = 240_000)]
    public async Task Unavailable_smtp_keeps_row_and_next_tick_after_backoff_delivers_it()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("outbox-retry");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            // Недоступный SMTP не меняет ответ: письмо уже в outbox.
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure("smtp.unavailable", "SMTP is unreachable.")));

        var failedTick = await app.RunEmailDispatchAsync();

        Assert.Equal(0, failedTick.Sent);
        Assert.Equal(1, failedTick.Retried);
        Assert.Empty(sender.Messages);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = Assert.Single(await db.EmailOutbox.AsNoTracking().ToListAsync());
            Assert.Equal(1, row.Attempts);
            Assert.Null(row.SentAt);
            Assert.Null(row.FailedAt);
            Assert.Equal("smtp.unavailable", row.LastError);
            Assert.True(row.NextAttemptAt > clock.UtcNow);

            // Backoff после первой неудачи — одна минута (точность timestamptz — микросекунды).
            Assert.Equal(
                clock.UtcNow.AddMinutes(1),
                row.NextAttemptAt,
                TimeSpan.FromMilliseconds(1));
        }

        // Строка ещё не созрела: немедленный тик её не берёт.
        var early = await app.RunEmailDispatchAsync();
        Assert.Equal(0, early.Sent);

        // Транспорт восстановился, backoff прошёл — письмо уходит.
        sender.Behavior = null;
        clock.SetUtcNow(clock.UtcNow.AddSeconds(61));

        var delivered = await app.RunEmailDispatchAsync();

        Assert.Equal(1, delivered.Sent);
        Assert.Single(sender.Messages);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var row = Assert.Single(await db.EmailOutbox.AsNoTracking().ToListAsync());
            Assert.Equal(1, row.Attempts);
            Assert.NotNull(row.SentAt);
            Assert.Null(row.FailedAt);
            Assert.Null(row.LastError);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Disabled_worker_keeps_rows_in_the_table()
    {
        var sender = new CapturingEmailSender();
        var configuration = ConfiguredEmail();
        configuration["EmailOutbox__Enabled"] = "false";

        await using var app = await TestApp.StartAsync(
            _database,
            configuration,
            emailSender: sender);

        var email = NewEmail("outbox-disabled");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var request = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
        }

        var tick = await app.RunEmailDispatchAsync();

        // Выключен воркер, а не запись: строка на месте и уйдёт после включения флага.
        Assert.Equal(0, tick.Sent);
        Assert.Empty(sender.Messages);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = Assert.Single(await db.EmailOutbox.AsNoTracking().ToListAsync());
        Assert.Null(row.SentAt);
        Assert.Null(row.FailedAt);
        Assert.Equal(0, row.Attempts);
    }

    [Fact(Timeout = 240_000)]
    public async Task Repeated_request_after_cooldown_creates_a_second_row()
    {
        var clock = new MutableTimeProvider();
        var sender = new CapturingEmailSender();

        await using var app = await TestApp.StartAsync(
            _database,
            ConfiguredEmail(),
            timeProvider: clock,
            emailSender: sender);

        var email = NewEmail("outbox-resend");
        await app.CreateUserAsync(email);

        using var anonymous = app.CreateClient();

        using (var first = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        clock.SetUtcNow(clock.UtcNow.AddSeconds(61));

        using (var second = await anonymous.PostAsJsonAsync(
                   "api/auth/password-reset",
                   new RequestPasswordResetRequest(email)))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }

        await app.RunEmailDispatchAsync();

        Assert.Equal(2, sender.Messages.Count);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.EmailOutbox.AsNoTracking().ToListAsync();

        // Новый токен — новый ключ дедупликации и новая строка; обе уже отправлены.
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.DeduplicationKey).Distinct().Count());
        Assert.All(rows, row => Assert.NotNull(row.SentAt));
    }

    private static Dictionary<string, string?> ConfiguredEmail()
        => new()
        {
            ["Email__Enabled"] = "true",
            ["Email__Host"] = "localhost",
            ["Email__Port"] = "1025",
            ["Email__UseTls"] = "false",
            ["Email__FromAddress"] = "no-reply@barbersearch.test",
            ["App__PublicUrl"] = PublicUrl
        };

    private static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@barbersearch.test";

    private static async Task SeedOutboxAsync(TestApp app, DateTimeOffset now, int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        for (var index = 0; index < count; index++)
        {
            var created = EmailOutboxMessage.Create(
                $"seed-{Guid.NewGuid():N}",
                NewEmail($"outbox-seed-{index}"),
                "Сброс пароля — BarberSearch",
                "<p><a href=\"http://localhost:5173/reset-password?token=seed\">Задать пароль</a></p>",
                "Задать новый пароль: http://localhost:5173/reset-password?token=seed",
                now);

            Assert.True(created.IsSuccess);
            db.EmailOutbox.Add(created.Value);
        }

        await db.SaveChangesAsync();
    }
}
