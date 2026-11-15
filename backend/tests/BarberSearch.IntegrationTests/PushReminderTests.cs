using BarberSearch.Application.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Push;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Push-напоминания на реальной PostgreSQL: окно «через час», дедупликация, отсутствие
/// устройства и режим разработки без ключей.
/// </summary>
/// <remarks>
/// Каждая строка создаётся доменными фабриками, а <c>created_at</c> брони сдвигается в прошлое
/// точечным <c>UPDATE</c>: правило «бронь создана не позже <c>start_at − lead</c>» иначе
/// отсчитывалось бы от текущей секунды и корректная бронь не попадала бы в окно.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushReminderTests
{
    private const string CustomerFirstName = "Пётр";
    private const string CustomerLastName = "Секретов";
    private const string CustomerPhone = "+7 999 000-11-22";
    private const string CustomerNote = "Секретная заметка клиента";

    private readonly PostgresFixture _database;

    public PushReminderTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Confirmed_booking_inside_the_window_creates_and_sends_the_push()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var customer = await SeedConfirmedBookingAsync(app);
        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);

        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushDelivery.Sent, row.Delivery);
        Assert.Equal("Salon Push Barber", row.SalonName);
        Assert.Equal("Push Barber", row.BarberName);
        Assert.Equal("Haircut Push Barber", row.ServiceName);
        Assert.Equal("Europe/Berlin", row.SalonTimeZoneId);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal("integration-device-token", captured.Token);
        Assert.Equal("Через час: салон «Salon Push Barber»", captured.Message.Title);
        Assert.StartsWith(
            "Мастер Push Barber · Haircut Push Barber · ",
            captured.Message.Body,
            StringComparison.Ordinal);

        Assert.True(SalonTimeZone.TryResolve(row.SalonTimeZoneId, out var zone));
        Assert.Contains(
            SalonTimeZone.FormatTimeOfDay(row.StartAt, zone),
            captured.Message.Body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(CustomerFirstName, captured.Message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerLastName, captured.Message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerPhone, captured.Message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerNote, captured.Message.Body, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Two_parallel_ticks_create_exactly_one_row()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var customer = await SeedConfirmedBookingAsync(app);
        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var results = await Task.WhenAll(
            app.RunPushTickAsync(),
            app.RunPushTickAsync());

        Assert.Equal(1, results.Sum(result => result.Created));
        Assert.Single(await push.GetNotificationsAsync(customer.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Booking_created_inside_the_window_is_not_pushed()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Barber");
        var customer = await app.CreateUserAsync(
            $"push-late-{Guid.NewGuid():N}@barbersearch.test");

        // Бронь на 59 минут вперёд, созданная только что: «через час» уже неправда.
        await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddMinutes(59),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Empty(await push.GetNotificationsAsync(customer.Id));
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task Without_a_registered_device_the_row_is_skipped()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var customer = await SeedConfirmedBookingAsync(app);
        var push = new PushTestData(app);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Skipped);

        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushDelivery.Skipped, row.Delivery);
        Assert.Equal("no registered device", row.LastError);
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task Without_credentials_outside_development_keeps_the_row_pending()
    {
        var sender = new CapturingPushSender { IsConfigured = false };

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var customer = await SeedConfirmedBookingAsync(app);
        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Sent);
        Assert.Equal(0, result.Skipped);

        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushDelivery.Pending, row.Delivery);
        Assert.Null(row.LastError);
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task Disabled_worker_creates_no_rows()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Push__Enabled"] = "false"
            },
            pushSender: sender);

        var customer = await SeedConfirmedBookingAsync(app);
        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var result = await app.RunPushTickAsync();

        Assert.Equal(PushTickResult.Empty, result);
        Assert.Empty(await push.GetNotificationsAsync(customer.Id));
        Assert.Empty(sender.Messages);
    }

    /// <summary>
    /// Создаёт подтверждённую бронь через 59 минут и сдвигает её <c>created_at</c> в прошлое.
    /// </summary>
    /// <remarks>
    /// 59, а не 20: окно «через час» с допуском на опоздание тика (5 минут при интервале
    /// 3600 с в тестовом хосте) должно быть почти полным, иначе напоминание справедливо
    /// пропускается как позднее.
    /// </remarks>
    private static async Task<User> SeedConfirmedBookingAsync(TestApp app)
    {
        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Barber");

        var customer = await app.CreateUserAsync(
            $"push-customer-{Guid.NewGuid():N}@barbersearch.test");
        await notifications.SetCustomerProfileAsync(
            customer.Id,
            CustomerFirstName,
            CustomerLastName,
            CustomerPhone);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddMinutes(59),
            BookingStatus.Confirmed,
            customerNote: CustomerNote);

        // «За час» требует, чтобы бронь существовала раньше окна напоминания.
        await data.SetBookingCreatedAtAsync(
            booking.Id,
            DateTimeOffset.UtcNow.AddHours(-2));

        return customer;
    }

    private static Dictionary<string, string?> PushEnabled()
        => new(StringComparer.Ordinal)
        {
            ["Push__Enabled"] = "true",
            ["Push__IntervalSeconds"] = "3600"
        };
}
