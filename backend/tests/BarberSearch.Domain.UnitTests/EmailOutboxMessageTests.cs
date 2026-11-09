using BarberSearch.Domain.EmailOutbox;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Домен строки outbox: валидация готового письма, граница готовности, отметка отправки
/// и учёт неудачных попыток с окончательным отказом.
/// </summary>
/// <remarks>
/// Домен часов не читает (R-27, AGENT.md §7.6): «сейчас» и момент следующей попытки всегда
/// приходят аргументом, поэтому тест не зависит от системного времени.
/// </remarks>
public class EmailOutboxMessageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string DeduplicationKey = "019a1f00-0000-7000-8000-000000000001";

    private const string Recipient = "client@example.com";

    private const string Subject = "Сброс пароля — BarberSearch";

    private const string HtmlBody =
        "<p><a href=\"http://localhost:5173/reset-password?token=abc_DEF-123\">Задать пароль</a></p>";

    private const string TextBody =
        "Задать новый пароль: http://localhost:5173/reset-password?token=abc_DEF-123";

    [Fact]
    public void Create_WithValidMessage_StartsReady()
    {
        var result = EmailOutboxMessage.Create(
            DeduplicationKey,
            Recipient,
            Subject,
            HtmlBody,
            TextBody,
            Now);

        Assert.True(result.IsSuccess);

        var message = result.Value;
        Assert.Equal(DeduplicationKey, message.DeduplicationKey);
        Assert.Equal(Recipient, message.ToEmail);
        Assert.Equal(Subject, message.Subject);
        Assert.Equal(HtmlBody, message.HtmlBody);
        Assert.Equal(TextBody, message.TextBody);
        Assert.Equal(Now, message.NextAttemptAt);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.SentAt);
        Assert.Null(message.FailedAt);
        Assert.Null(message.LastError);
        Assert.False(message.IsProcessed);
        Assert.True(message.IsReady(Now));
        Assert.NotEqual(Guid.Empty, message.Id);
    }

    [Theory]
    [InlineData("", Recipient, Subject)]
    [InlineData("   ", Recipient, Subject)]
    [InlineData("key", "", Subject)]
    [InlineData("key", "   ", Subject)]
    [InlineData("key", "not-an-email", Subject)]
    [InlineData("key", Recipient, "")]
    [InlineData("key", Recipient, "   ")]
    public void Create_WithBrokenKeyOrRecipientOrSubject_ReturnsValidationFailure(
        string deduplicationKey,
        string recipient,
        string subject)
    {
        var result = EmailOutboxMessage.Create(
            deduplicationKey,
            recipient,
            subject,
            HtmlBody,
            TextBody,
            Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithTooLongDeduplicationKey_ReturnsValidationFailure()
    {
        var result = EmailOutboxMessage.Create(
            new string('k', EmailOutboxMessage.MAX_DEDUPLICATION_KEY_LENGTH + 1),
            Recipient,
            Subject,
            HtmlBody,
            TextBody,
            Now);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailOutbox.create.deduplicationKey");
    }

    [Fact]
    public void Create_WithEmptyBodies_ReturnsValidationFailure()
    {
        var missingHtml = EmailOutboxMessage.Create(
            DeduplicationKey, Recipient, Subject, "   ", TextBody, Now);
        Assert.True(missingHtml.IsFailure);
        Assert.Contains(
            missingHtml.Error.Errors,
            error => error.Code == "emailOutbox.create.htmlBody");

        var missingText = EmailOutboxMessage.Create(
            DeduplicationKey, Recipient, Subject, HtmlBody, "", Now);
        Assert.True(missingText.IsFailure);
        Assert.Contains(
            missingText.Error.Errors,
            error => error.Code == "emailOutbox.create.textBody");
    }

    [Fact]
    public void Create_WithDefaultTime_ReturnsValidationFailure()
    {
        var result = EmailOutboxMessage.Create(
            DeduplicationKey,
            Recipient,
            Subject,
            HtmlBody,
            TextBody,
            default);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailOutbox.create.now");
    }

    [Fact]
    public void IsReady_IsStrictlyAboutTheScheduleAndProcessedState()
    {
        var message = CreateValid();

        Assert.False(message.IsReady(Now.AddSeconds(-1)));
        Assert.True(message.IsReady(Now));
        Assert.True(message.IsReady(Now.AddMinutes(1)));

        Assert.True(message.MarkSent(Now).IsSuccess);

        Assert.False(message.IsReady(Now.AddDays(1)));
    }

    [Fact]
    public void MarkSent_SetsSentAtClearsErrorAndIsIdempotent()
    {
        var message = CreateValid();
        Assert.True(message.RecordFailure(
            "smtp.unavailable",
            Now,
            Now.AddMinutes(1),
            maxAttempts: 5).IsSuccess);
        Assert.Equal("smtp.unavailable", message.LastError);

        Assert.True(message.MarkSent(Now.AddMinutes(2)).IsSuccess);

        Assert.Equal(Now.AddMinutes(2), message.SentAt);
        Assert.Null(message.FailedAt);
        Assert.Null(message.LastError);
        Assert.True(message.IsProcessed);

        // Повторная отметка не сдвигает время.
        Assert.True(message.MarkSent(Now.AddMinutes(3)).IsSuccess);
        Assert.Equal(Now.AddMinutes(2), message.SentAt);

        // Неудача после отправки не планирует новую попытку.
        Assert.True(message.RecordFailure(
            "smtp.unavailable",
            Now.AddMinutes(4),
            Now.AddMinutes(5),
            maxAttempts: 5).IsSuccess);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.FailedAt);
    }

    [Fact]
    public void RecordFailure_IncrementsAttemptsAndSchedulesNextAttempt()
    {
        var message = CreateValid();

        Assert.True(message.RecordFailure(
            "smtp.unavailable",
            Now,
            Now.AddMinutes(1),
            maxAttempts: 5).IsSuccess);

        Assert.Equal(1, message.Attempts);
        Assert.Equal("smtp.unavailable", message.LastError);
        Assert.Equal(Now.AddMinutes(1), message.NextAttemptAt);
        Assert.Null(message.SentAt);
        Assert.Null(message.FailedAt);
        Assert.False(message.IsReady(Now));

        Assert.True(message.IsReady(Now.AddMinutes(1)));
    }

    [Fact]
    public void RecordFailure_OnLastAttempt_MarksRowFailed()
    {
        var message = CreateValid();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.True(message.RecordFailure(
                "smtp.unavailable",
                Now,
                Now.AddMinutes(1),
                maxAttempts: 5).IsSuccess);
            Assert.Null(message.FailedAt);
        }

        Assert.True(message.RecordFailure(
            "smtp.unavailable",
            Now,
            Now.AddMinutes(1),
            maxAttempts: 5).IsSuccess);

        Assert.Equal(5, message.Attempts);
        Assert.Equal(Now, message.FailedAt);
        Assert.True(message.IsProcessed);
        Assert.False(message.IsReady(Now.AddDays(1)));

        // После окончательного отказа новая попытка не планируется.
        Assert.True(message.RecordFailure(
            "smtp.unavailable",
            Now.AddDays(1),
            Now.AddDays(1),
            maxAttempts: 5).IsSuccess);
        Assert.Equal(5, message.Attempts);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("smtp.unavailable", 0)]
    public void RecordFailure_WithInvalidArguments_ReturnsValidationFailure(
        string errorCode,
        int maxAttempts)
    {
        var message = CreateValid();

        var result = message.RecordFailure(errorCode, Now, Now.AddMinutes(1), maxAttempts);

        Assert.True(result.IsFailure);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void RecordFailure_WithNextAttemptInThePast_ReturnsValidationFailure()
    {
        var message = CreateValid();

        var result = message.RecordFailure(
            "smtp.unavailable",
            Now,
            Now.AddMinutes(-1),
            maxAttempts: 5);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "emailOutbox.recordFailure.nextAttemptAt");
    }

    private static EmailOutboxMessage CreateValid()
        => EmailOutboxMessage.Create(
            DeduplicationKey,
            Recipient,
            Subject,
            HtmlBody,
            TextBody,
            Now).Value;
}
