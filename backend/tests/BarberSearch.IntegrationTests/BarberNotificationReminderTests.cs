using BarberSearch.Application.Common;
using BarberSearch.Application.Notifications;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Notifications;
using BarberSearch.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Воркер напоминаний на реальной PostgreSQL: окно визита, дайджест, выключенный флаг,
/// повторный и параллельный тик, текст без ПДн.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberNotificationReminderTests
{
    private const long ChatId = 424_242;
    private const string PublicUrl = "https://cabinet.test";
    private const string CustomerFirstName = "Пётр";
    private const string CustomerLastName = "Секретов";
    private const string CustomerPhone = "+7 999 000-11-22";
    private const string CustomerNote = "Секретная заметка клиента";

    private readonly PostgresFixture _database;

    public BarberNotificationReminderTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Confirmed_booking_inside_lead_window_creates_upcoming_notification_and_message()
    {
        var telegram = new CapturingTelegramSender();

        await using var app = await TestApp.StartAsync(
            _database,
            RemindersWithTelegram(),
            telegramSender: telegram);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Reminder Barber");
        await notifications.LinkBarberProfileAsync(barber);

        var customer = await app.CreateUserAsync(
            $"reminder-customer-{Guid.NewGuid():N}@barbersearch.test");
        await notifications.SetCustomerProfileAsync(
            customer.Id,
            CustomerFirstName,
            CustomerLastName,
            CustomerPhone);

        var startAt = DateTimeOffset.UtcNow.AddMinutes(20);
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed,
            customerNote: CustomerNote);

        await notifications.SeedSettingsAsync(
            barber.User.Id,
            leadMinutes: 30,
            digestEnabled: false,
            chatId: ChatId);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider.GetRequiredService<IReminderSourceRepository>();
            var found = await source.GetUpcomingBookingsAsync(
                barber.User.Id,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(60),
                CancellationToken.None);
            Assert.True(found.IsSuccess, "source query failed");
            Assert.Single(found.Value);

            var settingsRepository = scope.ServiceProvider
                .GetRequiredService<IBarberNotificationSettingsRepository>();
            var enabled = await settingsRepository.GetEnabledAsync(CancellationToken.None);
            Assert.True(enabled.IsSuccess, "settings query failed");
            Assert.Contains(enabled.Value, item => item.UserId == barber.User.Id && item.IsEnabled);
        }

        var service = app.Services.GetRequiredService<ReminderDispatchService>();
        var result = await service.RunTickAsync(CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Sent);

        var rows = await notifications.GetNotificationsAsync(
            barber.User.Id,
            BarberNotificationKind.Upcoming);

        var row = Assert.Single(rows);
        Assert.Equal(booking.Id, row.BookingId);

        // PostgreSQL хранит timestamptz с микросекундной точностью, а DateTimeOffset —
        // со 100-наносекундной: после круга через базу сравниваем с допуском.
        Assert.NotNull(row.StartAt);
        AssertSameInstant(booking.StartAt, row.StartAt.Value);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal("Haircut Reminder Barber", row.ServiceName);
        Assert.Equal("Salon Reminder Barber", row.SalonName);
        Assert.Equal("Europe/Berlin", row.SalonTimeZoneId);
        Assert.Equal(BarberNotificationDelivery.Sent, row.Delivery);
        Assert.Null(row.ReadAt);

        Assert.True(SalonTimeZone.TryResolve("Europe/Berlin", out var salonZone));
        var expected = NotificationMessages.Upcoming(
            leadMinutes: 30,
            localTime: SalonTimeZone.FormatTimeOfDay(startAt, salonZone),
            serviceName: "Haircut Reminder Barber",
            salonName: "Salon Reminder Barber",
            cabinetUrl: PublicUrl + NotificationMessages.NOTIFICATIONS_PATH);

        var message = Assert.Single(telegram.Messages);
        Assert.Equal(ChatId, message.ChatId);
        Assert.Equal(expected, message.Text);

        Assert.DoesNotContain(CustomerFirstName, message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerLastName, message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerPhone, message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerNote, message.Text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Pending_booking_does_not_create_notification()
    {
        var telegram = new CapturingTelegramSender();

        await using var app = await TestApp.StartAsync(
            _database,
            RemindersWithTelegram(),
            telegramSender: telegram);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Pending Reminder Barber");
        await notifications.LinkBarberProfileAsync(barber);

        var customer = await app.CreateUserAsync(
            $"reminder-pending-{Guid.NewGuid():N}@barbersearch.test");

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddMinutes(20),
            BookingStatus.Pending);

        await notifications.SeedSettingsAsync(
            barber.User.Id,
            digestEnabled: false,
            chatId: ChatId);

        var service = app.Services.GetRequiredService<ReminderDispatchService>();
        var result = await service.RunTickAsync(CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Sent);
        Assert.Empty(await notifications.GetNotificationsAsync(barber.User.Id));
        Assert.Empty(telegram.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task Disabled_reminders_create_no_rows_and_send_no_messages()
    {
        var telegram = new CapturingTelegramSender();

        // Reminders:Enabled по умолчанию false в appsettings.json — конфигурацию не меняем.
        await using var app = await TestApp.StartAsync(_database, telegramSender: telegram);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Disabled Reminder Barber");
        await notifications.LinkBarberProfileAsync(barber);

        var customer = await app.CreateUserAsync(
            $"reminder-disabled-{Guid.NewGuid():N}@barbersearch.test");

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddMinutes(20),
            BookingStatus.Confirmed);

        await notifications.SeedSettingsAsync(
            barber.User.Id,
            digestEnabled: false,
            chatId: ChatId);

        var service = app.Services.GetRequiredService<ReminderDispatchService>();
        var result = await service.RunTickAsync(CancellationToken.None);

        Assert.Equal(ReminderTickResult.Empty, result);
        Assert.Empty(await notifications.GetNotificationsAsync(barber.User.Id));
        Assert.Empty(telegram.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task Two_parallel_ticks_create_exactly_one_notification()
    {
        await using var app = await TestApp.StartAsync(_database, RemindersEnabledOnly());

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Parallel Reminder Barber");
        await notifications.LinkBarberProfileAsync(barber);

        var customer = await app.CreateUserAsync(
            $"reminder-parallel-{Guid.NewGuid():N}@barbersearch.test");

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddMinutes(20),
            BookingStatus.Confirmed);

        await notifications.SeedSettingsAsync(barber.User.Id, digestEnabled: false);

        var service = app.Services.GetRequiredService<ReminderDispatchService>();

        // Два «экземпляра приложения» — два одновременных тика на одной базе. Уникальный
        // индекс ux_barber_notifications_upcoming должен оставить ровно одну строку.
        var results = await Task.WhenAll(
            service.RunTickAsync(CancellationToken.None),
            service.RunTickAsync(CancellationToken.None));

        Assert.Equal(1, results.Sum(result => result.Created));

        var rows = await notifications.GetNotificationsAsync(
            barber.User.Id,
            BarberNotificationKind.Upcoming);

        Assert.Single(rows);
    }

    [Fact(Timeout = 180_000)]
    public async Task Digest_is_created_in_primary_salon_time_zone_only_at_digest_minute()
    {
        var clock = new MutableTimeProvider();

        // Europe/Berlin 7 ноября 2026 — CET (UTC+1): 08:00 локального времени = 07:00 UTC.
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 7, 6, 59, 0, TimeSpan.Zero));

        await using var app = await TestApp.StartAsync(
            _database,
            RemindersEnabledOnly(),
            timeProvider: clock);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Digest Reminder Barber");
        await notifications.LinkBarberProfileAsync(barber);

        var customer = await app.CreateUserAsync(
            $"reminder-digest-{Guid.NewGuid():N}@barbersearch.test");

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero),
            BookingStatus.Confirmed);

        await notifications.SeedSettingsAsync(
            barber.User.Id,
            digestTime: new TimeOnly(8, 0),
            leadMinutes: 30);

        var service = app.Services.GetRequiredService<ReminderDispatchService>();

        // 07:59 локального времени: минутой раньше окна [08:00, 08:01).
        var before = await service.RunTickAsync(CancellationToken.None);
        Assert.Equal(0, before.Created);
        Assert.Empty(await notifications.GetNotificationsAsync(
            barber.User.Id,
            BarberNotificationKind.Digest));

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 7, 7, 0, 0, TimeSpan.Zero));

        var atDigest = await service.RunTickAsync(CancellationToken.None);
        Assert.Equal(1, atDigest.Created);

        var digest = Assert.Single(await notifications.GetNotificationsAsync(
            barber.User.Id,
            BarberNotificationKind.Digest));

        Assert.Equal(1, digest.BookingsCount);
        Assert.Equal(new DateOnly(2026, 11, 7), digest.DigestLocalDate);
        Assert.NotNull(digest.StartAt);
        AssertSameInstant(booking.StartAt, digest.StartAt.Value);
        Assert.Equal("Haircut Digest Reminder Barber", digest.ServiceName);
        Assert.Equal("Salon Digest Reminder Barber", digest.SalonName);
        Assert.Equal("Europe/Berlin", digest.SalonTimeZoneId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Digest_is_not_created_when_the_local_day_has_no_confirmed_bookings()
    {
        var clock = new MutableTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 7, 7, 0, 0, TimeSpan.Zero));

        await using var app = await TestApp.StartAsync(
            _database,
            RemindersEnabledOnly(),
            timeProvider: clock);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Empty Digest Barber");
        await notifications.LinkBarberProfileAsync(barber);

        await notifications.SeedSettingsAsync(
            barber.User.Id,
            digestTime: new TimeOnly(8, 0));

        var service = app.Services.GetRequiredService<ReminderDispatchService>();
        var result = await service.RunTickAsync(CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Empty(await notifications.GetNotificationsAsync(
            barber.User.Id,
            BarberNotificationKind.Digest));
    }

    /// <summary>
    /// Сравнивает моменты с допуском в одну миллисекунду: timestamptz округляет тики
    /// <see cref="DateTimeOffset"/> до микросекунд, и точное равенство после записи в базу
    /// не выполняется.
    /// </summary>
    private static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual)
        => Assert.True(
            Math.Abs((expected - actual).TotalMilliseconds) < 1,
            $"Expected {expected:O}, actual {actual:O}.");

    private static Dictionary<string, string?> RemindersEnabledOnly()
        => new(StringComparer.Ordinal)
        {
            ["Reminders__Enabled"] = "true"
        };

    private static Dictionary<string, string?> RemindersWithTelegram()
        => new(StringComparer.Ordinal)
        {
            ["Reminders__Enabled"] = "true",
            ["Telegram__BotToken"] = "123456:integration-test-token",
            ["Telegram__BotUsername"] = "integration_test_bot",
            ["Telegram__WebhookSecret"] = "integration-webhook-secret",
            ["App__PublicUrl"] = PublicUrl
        };
}
