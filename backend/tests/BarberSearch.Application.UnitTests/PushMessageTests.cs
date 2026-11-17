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

    [Fact]
    public void For_WithReminderKind_ReturnsTheSameReminderMessage()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(now.AddMinutes(30), "Барбершоп", "Иван", "Стрижка", SalonTimeZone);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Через час: салон «Барбершоп»", message.Title);
        Assert.Equal("Мастер Иван · Стрижка · сегодня в 17:30", message.Body);
        Assert.Equal(PushMessage.REMINDER_TYPE, message.Type);
    }

    [Fact]
    public void CreatedByCustomer_WithAllSnapshots_ReturnsTheExactRussianText()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Вы записались: салон «Барбершоп»", message.Title);
        Assert.Equal("Мастер Иван · Стрижка · сегодня в 17:30", message.Body);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, message.Type);
        Assert.Equal(BookingId, message.BookingId);
    }

    [Fact]
    public void CreatedByBarber_WithAllSnapshots_ReturnsTheExactRussianText()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByBarber);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Вас записали: салон «Барбершоп»", message.Title);
        Assert.Equal("Мастер Иван · Стрижка · сегодня в 17:30", message.Body);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, message.Type);
    }

    [Fact]
    public void CreatedByCustomer_WithoutSalon_UsesTheDegradedTitle()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            null,
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Вы записались к мастеру", message.Title);
    }

    [Fact]
    public void CreatedByBarber_WithoutSalon_UsesTheDegradedTitle()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "   ",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByBarber);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Вас записали к мастеру", message.Title);
    }

    [Fact]
    public void CreatedByCustomer_WithTomorrowSalonDate_UsesTomorrow()
    {
        // 18:30 UTC 15 ноября — это 23:30 по Екатеринбургу; визит через полчаса приходится
        // на 00:00 16 ноября по локальной дате салона, то есть «завтра».
        var now = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · завтра в 00:00", message.Body);
    }

    [Fact]
    public void CreatedByCustomer_WithFarDate_UsesTheGenitiveMonth()
    {
        // Горизонт записи — 30 дней, поэтому событие создания приходит и за месяц:
        // «24 ноября» в родительном падеже, без года.
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var startAt = new DateTimeOffset(2026, 12, 24, 9, 30, 0, TimeSpan.Zero);
        var notification = Create(
            startAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · 24 декабря в 14:30", message.Body);
    }

    [Fact]
    public void CreatedByCustomer_WithoutBarberAndService_OmitsThemWithoutNulls()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            null,
            null,
            "UTC",
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Вы записались: салон «Барбершоп»", message.Title);
        Assert.Equal("сегодня в 14:30", message.Body);
        Assert.DoesNotContain("null", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatedByCustomer_WithoutService_KeepsMasterAndTime()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            null,
            "UTC",
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Мастер Иван · сегодня в 14:30", message.Body);
    }

    [Fact]
    public void CreatedByCustomer_WithUnknownTimeZone_FallsBackToUtc()
    {
        var now = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            "Mars/Phobos",
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        Assert.Equal("Мастер Иван · Стрижка · сегодня в 14:30", message.Body);
    }

    [Fact]
    public void CreatedByCustomer_ContainsNoCustomerPersonalData()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = Create(
            now.AddMinutes(30),
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone,
            PushNotificationKind.BookingCreatedByCustomer);

        var message = PushMessages.For(notification, now);

        // Домен и текст не принимают ни имени, ни телефона, ни заметки клиента (152-ФЗ).
        Assert.Equal(
            "Вы записались: салон «Барбершоп» | Мастер Иван · Стрижка · сегодня в 17:30",
            $"{message.Title} | {message.Body}");
        Assert.DoesNotContain("+7", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Клиент", message.Body, StringComparison.Ordinal);
    }

    private static PushNotification Create(
        DateTimeOffset startAt,
        string? salonName,
        string? barberName,
        string? serviceName,
        string? salonTimeZoneId,
        PushNotificationKind kind = PushNotificationKind.Reminder)
        => PushNotification.Create(
            kind,
            UserId,
            BookingId,
            startAt,
            salonName,
            barberName,
            serviceName,
            salonTimeZoneId).Value;
}
