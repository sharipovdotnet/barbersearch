using BarberSearch.Application.Push;
using BarberSearch.Application.Push.DeliverPendingPushNotifications;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Тик доставки push-напоминаний: все устройства пользователя, мёртвые токены, повторы
/// и режим разработки без ключей.
/// </summary>
/// <remarks>
/// Часы стоят на 2026-11-15 12:00 UTC. Один тик — одна пачка; повторные попытки тест
/// двигает часами, а не ожиданием, поэтому расписание 1/5/15 минут проверяется целиком.
/// </remarks>
public class PushDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private const string SalonTimeZone = "Asia/Yekaterinburg";

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Deliver_WithOneDevice_MarksSentAndSendsTheExactRussianText()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Equal(0, result.Value.Failed);
        Assert.Equal(PushDelivery.Sent, notification.Delivery);

        var call = Assert.Single(context.Sender.Calls);
        Assert.Equal("device-1", call.Token);
        Assert.Equal("Через час: салон «Барбершоп»", call.Message.Title);
        Assert.Equal("Мастер Иван · Стрижка · сегодня в 17:20", call.Message.Body);
        Assert.Equal(booking.Id, call.Message.BookingId);
    }

    [Fact]
    public async Task Deliver_WithSeveralDevices_SendsOneRequestPerToken()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        context.Notifications.Seed(CreateNotification(booking.Id, startAt));
        context.Tokens.Seed(CreateToken("device-1"));
        context.Tokens.Seed(CreateToken("device-2"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(2, context.Sender.Calls.Count);
        Assert.Equal(1, context.Sender.Calls.Count(call => call.Token == "device-1"));
        Assert.Equal(1, context.Sender.Calls.Count(call => call.Token == "device-2"));
    }

    [Fact]
    public async Task Deliver_WithoutDevices_MarksSkipped()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(PushDelivery.Skipped, notification.Delivery);
        Assert.Equal("no registered device", notification.LastError);
        Assert.Empty(context.Sender.Calls);
    }

    [Fact]
    public async Task Deliver_WhenTheBookingWasCancelled_MarksSkipped()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        Assert.True(booking.Cancel().IsSuccess);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("booking is not confirmed", notification.LastError);
        Assert.Empty(context.Sender.Calls);
    }

    [Fact]
    public async Task Deliver_WhenTheBookingWasRescheduled_MarksSkipped()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        Assert.True(booking.Reschedule(Now.AddHours(2), Now.AddHours(3)).IsSuccess);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("booking was rescheduled", notification.LastError);
        Assert.Empty(context.Sender.Calls);
    }

    [Fact]
    public async Task Deliver_WhenTheBookingIsGone_MarksSkipped()
    {
        var context = CreateContext();
        var notification = CreateNotification(Guid.CreateVersion7(), Now.AddMinutes(20));
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("booking is not confirmed", notification.LastError);
    }

    [Fact]
    public async Task Deliver_WhenTheVisitAlreadyStarted_MarksSkipped()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(-1);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("window closed", notification.LastError);
        Assert.Empty(context.Sender.Calls);
    }

    [Fact]
    public async Task Deliver_WithTokenRejectedAsUnregistered_DeletesTheTokenAndMarksSkipped()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("dead-token"));
        context.Sender.Result = FakePushSender.TokenUnregistered();

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(PushDelivery.Skipped, notification.Delivery);
        Assert.Equal("no registered device", notification.LastError);
        Assert.Empty(context.Tokens.Items);
    }

    [Fact]
    public async Task Deliver_WithOneValidAndOneDeadToken_DeletesOnlyTheDeadOneAndMarksSent()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("dead-token"));
        context.Tokens.Seed(CreateToken("live-token"));
        context.Sender.OnSend = token => token == "dead-token"
            ? UnitResult.Failure(FakePushSender.TokenUnregistered())
            : UnitResult.Success<Failure>();

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(PushDelivery.Sent, notification.Delivery);
        var remaining = Assert.Single(context.Tokens.Items);
        Assert.Equal("live-token", remaining.Token);
    }

    [Fact]
    public async Task Deliver_WithTemporaryFailure_RetriesWithBackoffAndFailsAfterFiveAttempts()
    {
        var context = CreateContext();

        // Визит далеко в будущем: повторы занимают 36 минут и не должны упереться
        // в закрытое окно «через час» — иначе проверялось бы не расписание, а пропуск.
        var startAt = Now.AddHours(2);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));
        context.Sender.Result = FakePushSender.TemporaryFailure();

        var first = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(1, first.Value.Failed);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.Equal(1, notification.DeliveryAttempts);
        Assert.Equal(Now.AddMinutes(1), notification.NextAttemptAt);

        context.Clock.UtcNow = Now.AddMinutes(1);
        await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);
        Assert.Equal(2, notification.DeliveryAttempts);
        Assert.Equal(context.Clock.UtcNow.AddMinutes(5), notification.NextAttemptAt);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(5);
        await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);
        Assert.Equal(3, notification.DeliveryAttempts);
        Assert.Equal(context.Clock.UtcNow.AddMinutes(15), notification.NextAttemptAt);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);
        Assert.Equal(4, notification.DeliveryAttempts);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);

        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(5, notification.DeliveryAttempts);
        Assert.Equal(PushDelivery.Failed, notification.Delivery);
        Assert.Null(notification.NextAttemptAt);

        // Шестая попытка не берётся в пачку: исчерпанный лимит — терминальное состояние.
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(15);
        var exhausted = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.Equal(0, exhausted.Value.Failed);
        Assert.Equal(0, exhausted.Value.Sent);
    }

    [Fact]
    public async Task Deliver_WithCredentialsFailure_MarksFailedForRetry()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));
        context.Sender.Result = new Failure(Error.Failure(
            "push.unavailable",
            "Push credentials were rejected (HTTP 403)."));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Failed);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.Equal(Now.AddMinutes(1), notification.NextAttemptAt);
        Assert.Equal("push.unavailable", notification.LastError);
    }

    [Fact]
    public async Task Deliver_WhenPushIsNotConfigured_LogsTheTextOnceAndMarksSkipped()
    {
        var context = CreateContext(pushConfigured: false, devMode: true);
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(PushDelivery.Skipped, notification.Delivery);
        Assert.Equal("push is not configured", notification.LastError);
        Assert.Empty(context.Sender.Calls);

        Assert.Contains(
            context.Logger.Messages,
            message => message.Contains("Через час: салон «Барбершоп»", StringComparison.Ordinal)
                       && message.Contains(
                           "Мастер Иван · Стрижка · сегодня в 17:20",
                           StringComparison.Ordinal));
        Assert.All(
            context.Logger.Messages,
            message => Assert.DoesNotContain("device-1", message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deliver_WhenPushIsNotConfiguredOutsideDevelopment_KeepsTheRowPending()
    {
        var context = CreateContext(pushConfigured: false, devMode: false);
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("device-1"));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Equal(PushDelivery.Pending, notification.Delivery);
        Assert.Null(notification.LastError);
        Assert.Empty(context.Sender.Calls);
    }

    [Fact]
    public async Task Deliver_IgnoresIosTokensAndKeepsThemForTheApnsSlice()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        var notification = CreateNotification(booking.Id, startAt);
        context.Notifications.Seed(notification);
        context.Tokens.Seed(CreateToken("ios-device", PushPlatform.Ios));

        var result = await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Sent);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal("no registered device", notification.LastError);
        Assert.Empty(context.Sender.Calls);
        var remaining = Assert.Single(context.Tokens.Items);
        Assert.Equal(PushPlatform.Ios, remaining.Platform);
    }

    [Fact]
    public async Task Deliver_DoesNotLogTheDeviceToken()
    {
        var context = CreateContext();
        var startAt = Now.AddMinutes(20);
        var booking = CreateConfirmedBooking(startAt);
        context.Bookings.Add(booking);
        context.Notifications.Seed(CreateNotification(booking.Id, startAt));
        context.Tokens.Seed(CreateToken("very-secret-device-token"));

        await context.Handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            CancellationToken.None);

        Assert.All(
            context.Logger.Messages,
            message => Assert.DoesNotContain(
                "very-secret-device-token",
                message,
                StringComparison.Ordinal));
    }

    private static Booking CreateConfirmedBooking(DateTimeOffset startAt)
    {
        var booking = Booking.Create(
            UserId,
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

    private static PushNotification CreateNotification(Guid bookingId, DateTimeOffset startAt)
        => PushNotification.Create(
            UserId,
            bookingId,
            startAt,
            "Барбершоп",
            "Иван",
            "Стрижка",
            SalonTimeZone).Value;

    private static PushDeviceToken CreateToken(
        string token,
        PushPlatform platform = PushPlatform.Android)
        => PushDeviceToken.Register(UserId, token, platform, Now.AddMinutes(-5)).Value;

    private static Context CreateContext(bool pushConfigured = true, bool devMode = false)
    {
        var notifications = new FakePushNotificationRepository();
        var tokens = new FakePushDeviceTokenRepository();
        var bookings = new FakeBookingRepository();
        var sender = new FakePushSender { IsConfigured = pushConfigured };
        var clock = new FakeTimeProvider(Now);
        var logger = new RecordingLogger<DeliverPendingPushNotificationsHandler>();

        var handler = new DeliverPendingPushNotificationsHandler(
            notifications,
            tokens,
            bookings,
            sender,
            new PushOptions
            {
                Enabled = true,
                DevMode = devMode,
                BatchSize = 20,
                MaxAttempts = 5
            },
            clock,
            logger);

        return new Context(handler, notifications, tokens, bookings, sender, clock, logger);
    }

    private sealed record Context(
        DeliverPendingPushNotificationsHandler Handler,
        FakePushNotificationRepository Notifications,
        FakePushDeviceTokenRepository Tokens,
        FakeBookingRepository Bookings,
        FakePushSender Sender,
        FakeTimeProvider Clock,
        RecordingLogger<DeliverPendingPushNotificationsHandler> Logger);
}
