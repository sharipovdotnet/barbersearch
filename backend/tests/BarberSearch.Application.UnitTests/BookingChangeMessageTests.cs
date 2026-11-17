using System.Net;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Push;
using BarberSearch.Domain.Push;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Письмо клиенту об отмене и переносе записи, сделанных в приложении: точные тема и текстовое
/// тело, деградация без снимка салона и общий с push текст визита.
/// </summary>
/// <remarks>
/// Тема и текстовое тело — часть наблюдаемого поведения, поэтому проверяются байт в байт; HTML
/// содержит те же строки, но проверяется на вхождение, чтобы не ломаться от правки вёрстки.
/// Часовой пояс салона — Asia/Yekaterinburg (UTC+5), как в <c>PushMessageTests</c>: письмо и push
/// обязаны говорить одно и то же.
/// </remarks>
public class BookingChangeMessageTests
{
    private const string SalonTimeZone = "Asia/Yekaterinburg";

    private const string To = "client@barbersearch.test";

    [Fact]
    public void Message_CustomerCancelled_HasExactText()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var startAt = now.AddMinutes(30);
        var snapshot = new PushBookingSnapshot("Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = BookingChangeMessage.Create(
            To,
            snapshot,
            startAt,
            PushNotificationKind.BookingCancelledByCustomer,
            now);

        Assert.Equal(To, message.To);
        Assert.Equal("Запись отменена — Салон «Барбершоп»", message.Subject);
        Assert.Equal(
            "Вы отменили запись:\n"
            + "Мастер Иван · Стрижка · сегодня в 17:30\n"
            + "Салон «Барбершоп»",
            message.TextBody);
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Вы отменили запись:") + "</p>",
            ExtractParagraph(message.HtmlBody, 0));
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Мастер Иван · Стрижка · сегодня в 17:30") + "</p>",
            ExtractParagraph(message.HtmlBody, 1));
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Салон «Барбершоп»") + "</p>",
            ExtractParagraph(message.HtmlBody, 2));
    }

    [Fact]
    public void Message_CustomerCancelled_WithoutSalon_DegradesTheSubjectAndTheBody()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var startAt = now.AddMinutes(30);
        var snapshot = new PushBookingSnapshot(null, "Иван", "Стрижка", SalonTimeZone);

        var message = BookingChangeMessage.Create(
            To,
            snapshot,
            startAt,
            PushNotificationKind.BookingCancelledByCustomer,
            now);

        Assert.Equal("Запись отменена", message.Subject);
        Assert.Equal(
            "Вы отменили запись:\n"
            + "Мастер Иван · Стрижка · сегодня в 17:30",
            message.TextBody);
        Assert.DoesNotContain("Салон", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Салон", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_CustomerRescheduled_HasExactText()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var startAt = now.AddMinutes(30);
        var snapshot = new PushBookingSnapshot("Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = BookingChangeMessage.Create(
            To,
            snapshot,
            startAt,
            PushNotificationKind.BookingRescheduledByCustomer,
            now);

        Assert.Equal(To, message.To);
        Assert.Equal("Запись перенесена — Салон «Барбершоп»", message.Subject);
        Assert.Equal(
            "Вы перенесли запись:\n"
            + "Мастер Иван · Стрижка · сегодня в 17:30\n"
            + "Салон «Барбершоп»",
            message.TextBody);
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Вы перенесли запись:") + "</p>",
            ExtractParagraph(message.HtmlBody, 0));
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Мастер Иван · Стрижка · сегодня в 17:30") + "</p>",
            ExtractParagraph(message.HtmlBody, 1));
        Assert.Equal(
            "<p>" + WebUtility.HtmlEncode("Салон «Барбершоп»") + "</p>",
            ExtractParagraph(message.HtmlBody, 2));
    }

    [Fact]
    public void Message_CustomerRescheduled_WithoutSalon_DegradesTheSubjectAndTheBody()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var startAt = now.AddMinutes(30);
        var snapshot = new PushBookingSnapshot("   ", null, null, "UTC");

        var message = BookingChangeMessage.Create(
            To,
            snapshot,
            startAt,
            PushNotificationKind.BookingRescheduledByCustomer,
            now);

        Assert.Equal("Запись перенесена", message.Subject);
        Assert.Equal(
            "Вы перенесли запись:\n"
            + "сегодня в 12:30",
            message.TextBody);
        Assert.DoesNotContain("null", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Салон", message.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Достаёт абзац тела письма по порядку: нулевой — первая строка, дальше — визит и салон.
    /// </summary>
    private static string ExtractParagraph(string html, int index)
    {
        var paragraphs = html
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("<p>", StringComparison.Ordinal))
            .ToList();

        Assert.True(index < paragraphs.Count);

        return paragraphs[index];
    }
}
