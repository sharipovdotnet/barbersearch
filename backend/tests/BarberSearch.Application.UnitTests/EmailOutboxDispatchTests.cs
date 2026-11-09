using BarberSearch.Application.Emails;
using BarberSearch.Application.Emails.CleanupSentEmails;
using BarberSearch.Application.Emails.DispatchPendingEmails;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.EmailOutbox;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Диспетчер outbox: отправка пачки, ретраи с backoff, окончательный отказ и уборка.
/// </summary>
/// <remarks>
/// Транспорт и таблица — фейки: проверяется решение диспетчера, а не SMTP и не PostgreSQL.
/// Транзакция моделируется <see cref="FakeUnitOfWork"/>: <c>FOR UPDATE SKIP LOCKED</c>
/// проверяется интеграционным тестом, здесь важны отметки строк.
/// </remarks>
public class EmailOutboxDispatchTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string TransportErrorCode = "smtp.unavailable";

    [Fact]
    public async Task Dispatch_WithReadyRow_SendsItAndMarksSentInsideTransaction()
    {
        var context = new Context();
        var message = context.Seed();

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(0, result.Value.Retried);
        Assert.Equal(0, result.Value.Failed);

        // Диспетчер отправил письмо ровно один раз и отметил строку в той же транзакции.
        var delivered = Assert.Single(context.Sender.Messages);
        Assert.Equal(message.ToEmail, delivered.To);
        Assert.Equal(message.Subject, delivered.Subject);
        Assert.Equal(message.HtmlBody, delivered.HtmlBody);
        Assert.Equal(message.TextBody, delivered.TextBody);

        var row = Assert.Single(context.Outbox.Rows);
        Assert.Equal(Now, row.SentAt);
        Assert.Null(row.FailedAt);
        Assert.Null(row.LastError);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(1, context.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Dispatch_WhenTransportFails_IncrementsAttemptsAndSchedulesBackoff()
    {
        var context = new Context();
        context.Sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure(TransportErrorCode, "SMTP is unreachable.")));
        context.Seed();

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(1, result.Value.Retried);
        Assert.Equal(0, result.Value.Failed);
        Assert.Equal(1, context.UnitOfWork.CommitCount);

        var row = Assert.Single(context.Outbox.Rows);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(Now.AddMinutes(1), row.NextAttemptAt);
        Assert.Equal(TransportErrorCode, row.LastError);
        Assert.Null(row.SentAt);
        Assert.Null(row.FailedAt);

        // В лог попали только id строки, попытка и код ошибки — без адреса и тела.
        var warning = Assert.Single(context.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(row.Id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(row.ToEmail, warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 60)]
    public async Task Dispatch_RetryScheduleIsOneFiveFifteenMinutesOneHour(
        int previousAttempts,
        int expectedMinutes)
    {
        var context = new Context();
        context.Sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure(TransportErrorCode, "SMTP is unreachable.")));
        context.Seed(previousAttempts: previousAttempts);

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Retried);

        var row = Assert.Single(context.Outbox.Rows);
        Assert.Equal(previousAttempts + 1, row.Attempts);
        Assert.Equal(Now.AddMinutes(expectedMinutes), row.NextAttemptAt);
        Assert.Null(row.FailedAt);
    }

    [Fact]
    public async Task Dispatch_AfterMaxAttempts_MarksRowFailedAndLogsError()
    {
        var context = new Context(new EmailOutboxOptions { BatchSize = 10, MaxAttempts = 5 });
        context.Sender.Behavior = _ => UnitResult.Failure(
            new Failure(Error.Failure(TransportErrorCode, "SMTP is unreachable.")));
        context.Seed(previousAttempts: 4);

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Retried);
        Assert.Equal(1, result.Value.Failed);

        var row = Assert.Single(context.Outbox.Rows);
        Assert.Equal(5, row.Attempts);
        Assert.Equal(Now, row.FailedAt);
        Assert.Null(row.SentAt);
        Assert.Equal(TransportErrorCode, row.LastError);

        // Переход в failed — одна error-запись с id строки и кодом, без PII.
        var error = Assert.Single(context.Logger.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains(row.Id.ToString(), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(row.ToEmail, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatch_SkipsRowsThatAreNotDueYet()
    {
        var context = new Context();
        context.Seed(nextAttemptAt: Now.AddMinutes(5));

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(0, result.Value.Retried);
        Assert.Empty(context.Sender.Messages);

        var row = Assert.Single(context.Outbox.Rows);
        Assert.Null(row.SentAt);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Dispatch_DoesNotTouchRowsThatAreAlreadyProcessed()
    {
        var context = new Context();
        var sent = context.Seed();
        Assert.True(sent.MarkSent(Now.AddMinutes(-5)).IsSuccess);

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Empty(context.Sender.Messages);
    }

    [Fact]
    public async Task Dispatch_WhenBatchIsEmpty_DoesNotSendAndCommitsEmptyTick()
    {
        var context = new Context();

        var result = await context.Handler.Handle(
            new DispatchPendingEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DispatchPendingEmailsResult.Empty, result.Value);

        // Отправлять было нечего: транзакция открылась и закрылась без записи.
        Assert.Equal(0, context.Sender.SendCallCount);
        Assert.Empty(context.Outbox.Rows);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyProcessedRowsOlderThanRetention()
    {
        var context = new Context();
        var sentLongAgo = context.Seed();
        Assert.True(sentLongAgo.MarkSent(Now.AddDays(-10)).IsSuccess);

        var failedLongAgo = context.Seed();
        Assert.True(failedLongAgo.RecordFailure(
            TransportErrorCode,
            Now.AddDays(-9),
            Now.AddDays(-9),
            maxAttempts: 1).IsSuccess);

        var sentRecently = context.Seed();
        Assert.True(sentRecently.MarkSent(Now.AddDays(-1)).IsSuccess);

        var pending = context.Seed();

        var handler = new CleanupSentEmailsHandler(
            context.Outbox,
            new EmailOutboxOptions { RetentionDays = 7 },
            context.Clock,
            context.CleanupLogger);

        var result = await handler.Handle(
            new CleanupSentEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Deleted);

        var remaining = context.Outbox.Rows.Select(row => row.Id).ToList();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(sentRecently.Id, remaining);
        Assert.Contains(pending.Id, remaining);
        Assert.DoesNotContain(sentLongAgo.Id, remaining);
        Assert.DoesNotContain(failedLongAgo.Id, remaining);
    }

    [Fact]
    public async Task Cleanup_WithoutProcessedRows_DeletesNothing()
    {
        var context = new Context();
        context.Seed();

        var handler = new CleanupSentEmailsHandler(
            context.Outbox,
            new EmailOutboxOptions(),
            context.Clock,
            context.CleanupLogger);

        var result = await handler.Handle(
            new CleanupSentEmailsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Deleted);
        Assert.Single(context.Outbox.Rows);
    }

    /// <summary>
    /// Общий сценарий: фейковая таблица, транспорт, часы и журнал.
    /// </summary>
    private sealed class Context
    {
        public Context(EmailOutboxOptions? options = null)
        {
            Options = options ?? new EmailOutboxOptions { BatchSize = 10, MaxAttempts = 5 };

            UnitOfWork = new FakeUnitOfWork(Outbox);
            Outbox.IsInTransaction = () => UnitOfWork.IsInTransaction;

            Handler = new DispatchPendingEmailsHandler(
                Outbox,
                Sender,
                UnitOfWork,
                Options,
                Clock,
                Logger);
        }

        public EmailOutboxOptions Options { get; }

        public FakeEmailOutboxRepository Outbox { get; } = new();

        public FakeEmailSender Sender { get; } = new();

        public FakeTimeProvider Clock { get; } = new(Now);

        public FakeUnitOfWork UnitOfWork { get; }

        public CapturingLogger<DispatchPendingEmailsHandler> Logger { get; } = new();

        public CapturingLogger<CleanupSentEmailsHandler> CleanupLogger { get; } = new();

        public DispatchPendingEmailsHandler Handler { get; }

        /// <summary>
        /// Кладёт в «таблицу» строку письма с заданным числом прошлых попыток.
        /// </summary>
        public EmailOutboxMessage Seed(
            int previousAttempts = 0,
            DateTimeOffset? nextAttemptAt = null)
        {
            // Время создания задаёт и план первой попытки: так тест делает строку
            // «ещё не готовой» или «уже созревшей», не трогая приватные сеттеры.
            var creationTime = nextAttemptAt ?? Now;

            var created = EmailOutboxMessage.Create(
                Guid.NewGuid().ToString("D"),
                "client@example.com",
                "Подтвердите адрес электронной почты — BarberSearch",
                """
                <p>Здравствуйте!</p>
                <p><a href="http://localhost:5173/verify-email?token=abc_DEF-123">Подтвердить адрес</a></p>
                """,
                "Здравствуйте!\n\nПодтвердить адрес: http://localhost:5173/verify-email?token=abc_DEF-123",
                creationTime);

            Assert.True(created.IsSuccess);
            var message = created.Value;

            for (var attempt = 0; attempt < previousAttempts; attempt++)
            {
                Assert.True(message.RecordFailure(
                    TransportErrorCode,
                    creationTime,
                    creationTime,
                    maxAttempts: 10).IsSuccess);
            }

            Outbox.AddRaw(message);

            return message;
        }
    }

    /// <summary>
    /// Журнал в памяти: тест проверяет уровень и текст записей диспетчера.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
