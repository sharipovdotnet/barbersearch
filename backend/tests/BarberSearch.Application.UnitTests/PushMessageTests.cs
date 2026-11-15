using BarberSearch.Application.Push;
using BarberSearch.Domain.Push;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Русский текст push-напоминания: точные строки, слово «сегодня»/«завтра» по поясу салона
/// и деградация при отсутствующих снимках.
/// </summary>
/// <remarks>
/// Текст — часть наблюдаемого поведения, поэтому проверяется байт в байт. Часовой пояс
/// салона — Asia/Yekaterinburg (UTC+5): полночь по UTC и по салону различаются, и тест
/// с пересечением суток ловит ошибку «слово считается по серверу».
/// </remarks>
public class PushMessageTests
{
    private const string SalonTimeZone = "Asia/Yekaterinburg";

    private static readonly Guid UserId = Guid.CreateVersion7();

    private static readonly Guid BookingId = Guid.CreateVersion7();

    [Fact]
    public void Reminder_WithAllSnapshots_ReturnsTheExactRussianText()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Через час: салон «Барбершоп»", message.Title);
        Assert.Equal("Мастер Иван · Стрижка · сегодня в 17:30", message.Body);
        Assert.Equal(BookingId, message.BookingId);
    }

    [Fact]
    public void Reminder_CrossingSalonMidnight_UsesTomorrow()
    {
        // 23:30 в Екатеринбурге; визит через полчаса — уже следующие локальные сутки.
        var now = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · завтра в 00:00", message.Body);
    }

    [Fact]
    public void Reminder_WithUtcDateCrossedButSameSalonDay_UsesToday()
    {
        // 01:00 и 01:30 по Екатеринбургу — уже 16-е число по салону, но одни сутки.
        var now = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · сегодня в 01:30", message.Body);
    }

    [Fact]
    public void Reminder_WithoutSalon_UsesTheDegradedTitle()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), null, "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Через час — визит к мастеру", message.Title);
    }

    [Fact]
    public void Reminder_WithoutBarberAndService_OmitsThemWithoutNulls()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", null, null, "UTC");

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Через час: салон «Барбершоп»", message.Title);
        Assert.Equal("сегодня в 14:30", message.Body);
        Assert.DoesNotContain("null", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reminder_WithoutService_KeepsMasterAndTime()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", null, "UTC");

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Мастер Иван · сегодня в 14:30", message.Body);
    }

    [Fact]
    public void Reminder_WithUnknownTimeZone_FallsBackToUtc()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Mars/Phobos");

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · сегодня в 14:30", message.Body);
    }

    [Fact]
    public void Reminder_WithoutAnySnapshot_StillHasTimeAndDegradedTitle()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), null, null, null, null);

        var message = PushMessages.Reminder(notification, now);

        Assert.Equal("Через час — визит к мастеру", message.Title);
        Assert.Equal("сегодня в 14:30", message.Body);
    }

    [Fact]
    public void Reminder_ContainsNoCustomerPersonalData()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.Reminder(notification, now);

        // Домен и текст не принимают ни имени, ни телефона, ни заметки клиента (152-ФЗ):
        // проверяем, что в сообщении нет ни одного из этих значений по построению.
        Assert.Equal(
            "Через час: салон «Барбершоп» | Мастер Иван · Стрижка · сегодня в 17:30",
            $"{message.Title} | {message.Body}");
        Assert.DoesNotContain("+7", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Клиент", message.Body, StringComparison.Ordinal);
    }

    private static PushNotification Create(
        DateTimeOffset startAt,
        string? salonName,
        string? barberName,
        string? serviceName,
        string? salonTimeZoneId)
        => PushNotification.Create(
            UserId,
            BookingId,
            startAt,
            salonName,
            barberName,
            serviceName,
            salonTimeZoneId).Value;
}
