using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.DeliverPendingNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Тик доставки: пропуск по каналу и по отменённой брони, расписание повторов и реакция
/// на <c>403</c> от Telegram (§3.2, §9 спецификации варианта C).
/// </summary>
public class NotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    private const long ChatId = 4242;

    private const string PublicUrl = "https://barber.example";

    private const string SalonTimeZone = "Asia/Yekaterinburg";

    [Fact]
    public async Task Deliver_WithLinkedChat_MarksSentAndSendsTextWithoutCustomerData()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Equal(0, result.Value.Failed);
        Assert.Equal(BarberNotificationDelivery.Sent, notification.Delivery);

        var call = Assert.Single(context.Telegram.Calls);
        Assert.Equal(ChatId, call.ChatId);
        Assert.Equal(
            "Через 30 минут запись: 08:20, стрижка, «Барбершоп на Ленина». "
            + "Открыть кабинет: https://barber.example/barber/notifications",
            call.Text);
    }

    [Fact]
    public async Task Deliver_WithoutLinkedChat_MarksSkippedAndKeepsTheInboxRow()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: false, leadMinutes: 30);

        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(BarberNotificationDelivery.Skipped, notification.Delivery);
        Assert.Empty(context.Telegram.Calls);
        Assert.Single(context.Notifications.Items);
    }

    [Fact]
    public async Task Deliver_WhenNotificationsDisabled_MarksSkipped()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: true, leadMinutes: 30, isEnabled: false);

        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(BarberNotificationDelivery.Skipped, notification.Delivery);
        Assert.Empty(context.Telegram.Calls);
    }

    [Fact]
    public async Task Deliver_WhenBookingCancelledBetweenCreationAndDelivery_MarksSkipped()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var booking = CreateConfirmedBooking(startAt);
        Assert.True(booking.Cancel().IsSuccess);
        context.Bookings.Add(booking);

        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(BarberNotificationDelivery.Skipped, notification.Delivery);
        Assert.Equal("booking is not confirmed", notification.LastError);
        Assert.Empty(context.Telegram.Calls);
    }

    [Fact]
    public async Task Deliver_WhenBookingIsGone_MarksSkipped()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var notification = CreateUpcoming(userId, Now.AddMinutes(20), Guid.CreateVersion7());
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Empty(context.Telegram.Calls);
    }

    [Fact]
    public async Task Deliver_WithTemporaryFailure_RetriesWithBackoffAndFailsAfterFiveAttempts()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        context.Telegram.Result = FakeTelegramSender.TemporaryFailure();

        // Попытка 1 → повтор через 1 минуту, состояние остаётся Pending.
        var first = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(1, first.Value.Failed);
        Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);
        Assert.Equal(1, notification.DeliveryAttempts);
        Assert.Equal(Now.AddMinutes(1), notification.NextAttemptAt);

        // Попытка 2 → повтор через 5 минут.
        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Handler.Handle(new DeliverPendingNotificationsCommand(), CancellationToken.None);

        Assert.Equal(2, notification.DeliveryAttempts);
        Assert.Equal(context.Clock.UtcNow.AddMinutes(5), notification.NextAttemptAt);

        // Попытка 3 → повтор через 15 минут.
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        await context.Handler.Handle(new DeliverPendingNotificationsCommand(), CancellationToken.None);

        Assert.Equal(3, notification.DeliveryAttempts);
        Assert.Equal(context.Clock.UtcNow.AddMinutes(15), notification.NextAttemptAt);

        // Попытка 4 — всё ещё Pending и всё ещё 15 минут.
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        await context.Handler.Handle(new DeliverPendingNotificationsCommand(), CancellationToken.None);

        Assert.Equal(4, notification.DeliveryAttempts);
        Assert.Equal(BarberNotificationDelivery.Pending, notification.Delivery);

        // Попытка 5 — окончательный Failed, расписание очищено.
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        await context.Handler.Handle(new DeliverPendingNotificationsCommand(), CancellationToken.None);

        Assert.Equal(Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS, notification.DeliveryAttempts);
        Assert.Equal(BarberNotificationDelivery.Failed, notification.Delivery);
        Assert.Null(notification.NextAttemptAt);

        // Шестая попытка не берётся в пачку: исчерпанный лимит — терминальное состояние.
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        var exhausted = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(0, exhausted.Value.Failed);
        Assert.Equal(0, exhausted.Value.Sent);
    }

    [Fact]
    public async Task Deliver_WhenTelegramRejectsChat_MarksSkippedAndUnlinksTelegram()
    {
        var userId = Guid.CreateVersion7();
        var startAt = Now.AddMinutes(20);
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateUpcoming(userId, startAt, booking.Id);
        context.Notifications.Seed(notification);

        context.Telegram.Result = FakeTelegramSender.ChatUnavailable();

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(0, result.Value.Failed);
        Assert.Equal(BarberNotificationDelivery.Skipped, notification.Delivery);

        var settings = Assert.Single(context.Settings.All);
        Assert.Null(settings.TelegramChatId);
        Assert.False(settings.IsTelegramLinked);
    }

    [Fact]
    public async Task Deliver_Digest_UsesBookingsCountAndFirstVisitTime()
    {
        var userId = Guid.CreateVersion7();
        var context = CreateContext(userId, linked: true, leadMinutes: 30);

        var firstStartAt = new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);
        var digest = BarberNotification.Digest(
            userId,
            4,
            new DateOnly(2026, 11, 10),
            firstStartAt,
            "стрижка",
            "Барбершоп на Ленина",
            SalonTimeZone).Value;

        context.Notifications.Seed(digest);

        var result = await context.Handler.Handle(
            new DeliverPendingNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);

        var call = Assert.Single(context.Telegram.Calls);
        Assert.Equal(
            "Сегодня 4 записи, первая в 10:00. "
            + "Открыть кабинет: https://barber.example/barber/notifications",
            call.Text);
    }

    private static BarberNotification CreateUpcoming(
        Guid userId,
        DateTimeOffset startAt,
        Guid bookingId)
        => BarberNotification.Upcoming(
            userId,
            bookingId,
            startAt,
            "стрижка",
            "Барбершоп на Ленина",
            SalonTimeZone).Value;

    private static Booking CreateConfirmedBooking(DateTimeOffset startAt)
    {
        var booking = Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;

        Assert.True(booking.Confirm().IsSuccess);

        return booking;
    }

    private static Context CreateContext(
        Guid userId,
        bool linked,
        int leadMinutes,
        bool isEnabled = true)
    {
        var notifications = new FakeBarberNotificationRepository();
        var settings = new FakeBarberNotificationSettingsRepository();
        var bookings = new FakeBookingRepository();
        var telegram = new FakeTelegramSender();
        var clock = new FakeTimeProvider(Now);

        var userSettings = BarberNotificationSettings.CreateDefault(userId).Value;
        Assert.True(userSettings.Update(isEnabled, true, new TimeOnly(8, 0), leadMinutes).IsSuccess);

        if (linked)
        {
            Assert.True(userSettings.LinkTelegram(ChatId, Now).IsSuccess);
        }

        settings.Seed(userSettings);

        var handler = new DeliverPendingNotificationsHandler(
            notifications,
            settings,
            bookings,
            telegram,
            new RemindersOptions { BatchSize = 100 },
            new AppOptions { PublicUrl = PublicUrl },
            clock,
            NullLogger<DeliverPendingNotificationsHandler>.Instance);

        return new Context(handler, notifications, settings, bookings, telegram, clock);
    }

    private sealed record Context(
        DeliverPendingNotificationsHandler Handler,
        FakeBarberNotificationRepository Notifications,
        FakeBarberNotificationSettingsRepository Settings,
        FakeBookingRepository Bookings,
        FakeTelegramSender Telegram,
        FakeTimeProvider Clock);
}
