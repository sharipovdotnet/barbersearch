using System.Net;
using System.Net.Http.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Push;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Push;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Событие создания брони на реальной PostgreSQL через API: самозапись клиента, ручная запись
/// барбером, гость без push и выключенный канал.
/// </summary>
/// <remarks>
/// Проверяется весь путь «POST api/bookings или api/barber/bookings → строка очереди в той же
/// транзакции → доставка на следующем тике»: строка читается из БД, а сообщение — из
/// <see cref="CapturingPushSender"/>. Напоминание и событие создания одной брони не конфликтуют
/// благодаря уникальности тройки <c>(booking_id, kind, start_at)</c>.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushBookingCreatedTests
{
    private readonly PostgresFixture _database;

    public PushBookingCreatedTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBooking_ThroughTheCustomerApi_EnqueuesTheCustomerCreatedEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Created Barber");

        var email = $"push-self-booking-{Guid.NewGuid():N}@barbersearch.test";
        var customerId = await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customerId, "integration-device-token");

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        var bookingId = await CreateBookingAsync(app, barber, startAt);

        var result = await app.RunPushTickAsync();

        // Напоминание не создаётся: самозапись ещё Pending и визит далеко за окном.
        // Клиентское событие доставляется, а событие мастеру ждёт устройства барбера.
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Skipped);

        var row = Assert.Single(await push.GetNotificationsAsync(customerId));
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, row.Kind);
        Assert.Equal(bookingId, row.BookingId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);
        Assert.Equal("Salon Push Created Barber", row.SalonName);
        Assert.Equal("Push Created Barber", row.BarberName);
        Assert.Equal("Haircut Push Created Barber", row.ServiceName);
        Assert.Equal("Europe/Berlin", row.SalonTimeZoneId);

        // Вторая сторона события — строка мастеру с его именем клиента.
        var barberRow = Assert.Single(await push.GetNotificationsAsync(barber.User.Id));
        Assert.Equal(PushNotificationKind.BarberBookingCreated, barberRow.Kind);
        Assert.Equal(bookingId, barberRow.BookingId);
        Assert.Equal(barber.User.Id, barberRow.UserId);
        Assert.Equal("Integration Test", barberRow.CustomerName);
        Assert.Equal(PushDelivery.Skipped, barberRow.Delivery);
        Assert.Equal("no registered device", barberRow.LastError);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal("integration-device-token", captured.Token);
        Assert.Equal("Вы записались: салон «Salon Push Created Barber»", captured.Message.Title);
        Assert.StartsWith(
            "Мастер Push Created Barber · Haircut Push Created Barber · ",
            captured.Message.Body,
            StringComparison.Ordinal);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBooking_ThroughTheCustomerApi_DeliversTheMasterEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Master Event Barber");

        var email = $"push-master-event-{Guid.NewGuid():N}@barbersearch.test";
        var customerId = await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customerId, "customer-device-token");
        await push.SeedDeviceTokenAsync(barber.User.Id, "barber-device-token");

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        var bookingId = await CreateBookingAsync(app, barber, startAt);

        var result = await app.RunPushTickAsync();

        // Обе стороны получают своё событие: клиент — «вы записались», мастер — «новая запись».
        Assert.Equal(2, result.Sent);
        Assert.Equal(0, result.Skipped);

        var captured = Assert.Single(
            sender.Messages,
            message => message.Token == "barber-device-token");
        Assert.Equal("Новая запись: Integration Test", captured.Message.Title);
        Assert.Equal(PushMessage.BARBER_BOOKING_CREATED_TYPE, captured.Message.Type);
        Assert.Equal(bookingId, captured.Message.BookingId);
        Assert.StartsWith("Haircut Push Master Event Barber · ", captured.Message.Body);
        Assert.EndsWith(
            " · салон «Salon Push Master Event Barber»",
            captured.Message.Body,
            StringComparison.Ordinal);

        var barberRow = Assert.Single(
            await push.GetNotificationsAsync(barber.User.Id),
            notification => notification.Kind == PushNotificationKind.BarberBookingCreated);
        Assert.Equal(PushDelivery.Sent, barberRow.Delivery);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBarberBooking_ThroughTheBarberApi_EnqueuesTheBarberCreatedEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Manual Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(
            $"push-manual-customer-{Guid.NewGuid():N}@barbersearch.test");

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(barber.User.Email.Value);

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        using var created = await app.Client.PostAsJsonAsync(
            "api/barber/bookings",
            new CreateBarberBookingRequest(
                CustomerId: customer.Id,
                GuestFirstName: null,
                GuestLastName: null,
                GuestPhone: null,
                BarberServiceId: barber.Service.Id,
                SalonId: barber.Salon.Id,
                StartAt: startAt,
                EndAt: startAt.AddMinutes(30),
                IdempotencyKey: $"push-manual-{Guid.NewGuid():N}",
                CustomerNote: null));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);

        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushNotificationKind.BookingCreatedByBarber, row.Kind);
        Assert.Equal(body.Id, row.BookingId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        // Ручную запись внёс сам мастер: события мастеру нет.
        Assert.Empty(await push.GetNotificationsAsync(barber.User.Id));

        var captured = Assert.Single(sender.Messages);
        Assert.Equal("Вас записали: салон «Salon Push Manual Barber»", captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBarberBooking_ForAGuest_EnqueuesNothing()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Guest Barber");
        await LinkBarberProfileAsync(app, barber);

        await app.AuthenticateAsync(barber.User.Email.Value);

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        using var created = await app.Client.PostAsJsonAsync(
            "api/barber/bookings",
            new CreateBarberBookingRequest(
                CustomerId: null,
                GuestFirstName: "Гость",
                GuestLastName: null,
                GuestPhone: "+70000000000",
                BarberServiceId: barber.Service.Id,
                SalonId: barber.Salon.Id,
                StartAt: startAt,
                EndAt: startAt.AddMinutes(30),
                IdempotencyKey: $"push-guest-{Guid.NewGuid():N}",
                CustomerNote: null));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Sent);
        Assert.Equal(0, result.Skipped);

        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBooking_WhenPushIsDisabled_EnqueuesNothing()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Push__Enabled"] = "false",
                ["Push__IntervalSeconds"] = "3600"
            },
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Disabled Barber");

        var email = $"push-disabled-{Guid.NewGuid():N}@barbersearch.test";
        await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        await CreateBookingAsync(app, barber, startAt);

        var result = await app.RunPushTickAsync();

        Assert.Equal(PushTickResult.Empty, result);

        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());
        Assert.Empty(sender.Messages);
    }

    private static async Task<Guid> CreateBookingAsync(
        TestApp app,
        SeededBarber barber,
        DateTimeOffset startAt)
    {
        using var response = await app.Client.PostAsJsonAsync(
            "api/bookings",
            new CreateBookingRequest(
                barber.Profile.Id,
                barber.Service.Id,
                barber.Salon.Id,
                startAt,
                startAt.AddMinutes(30),
                $"push-create-{Guid.NewGuid():N}",
                "integration test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }

    private static Dictionary<string, string?> PushEnabled()
        => new(StringComparer.Ordinal)
        {
            ["Push__Enabled"] = "true",
            ["Push__IntervalSeconds"] = "3600"
        };
}
