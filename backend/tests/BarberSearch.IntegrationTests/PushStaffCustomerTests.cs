using System.Net;
using System.Net.Http.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Push;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Регрессия: клиент с ролью <see cref="UserRole.Barber"/> или <see cref="UserRole.Admin"/>
/// получает те же клиентские push-уведомления, что и обычный клиент, а мастер записи — своё
/// событие.
/// </summary>
/// <remarks>
/// Роль пользователя не участвует в гейтах уведомителей: получатель клиентского события —
/// всегда <c>Booking.UserId</c>, поэтому сотрудник, записавшийся как клиент, должен получать
/// «Вы записались», напоминание и «Ваша запись подтверждена». Тесты фиксируют это поведение
/// на реальной PostgreSQL через настоящий конвейер API: строка читается из БД, а текст
/// сообщения — из <see cref="CapturingPushSender"/>. Вторая сторона события создания —
/// строка мастеру у владельца профиля, независимо от того, кто записался.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushStaffCustomerTests
{
    private const string DeviceToken = "integration-device-token";

    private readonly PostgresFixture _database;

    public PushStaffCustomerTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBooking_AsBarberCustomer_DeliversTheCustomerCreatedEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Staff Barber Customer");

        // Сотрудник с ролью барбера записывается как обычный клиент: без X-Client-Surface.
        var staffCustomer = await app.CreateUserAsync(
            $"push-staff-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(staffCustomer.Email.Value);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(staffCustomer.Id, DeviceToken);

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        var bookingId = await CreateBookingAsync(app, barber, startAt);

        var result = await app.RunPushTickAsync();

        // Напоминания нет: самозапись ещё Pending и визит далеко за окном. Клиентское
        // событие уходит на устройство сотрудника, событие мастеру ждёт его устройства.
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Skipped);

        var row = Assert.Single(await push.GetNotificationsAsync(staffCustomer.Id));
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, row.Kind);
        Assert.Equal(bookingId, row.BookingId);
        Assert.Equal(staffCustomer.Id, row.UserId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);
        Assert.Equal("Salon Push Staff Barber Customer", row.SalonName);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(DeviceToken, captured.Token);
        Assert.Equal(
            "Вы записались: салон «Salon Push Staff Barber Customer»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, captured.Message.Type);

        // Вторая сторона события — строка мастеру: роль записавшегося её не отменяет.
        var barberRow = Assert.Single(await push.GetNotificationsAsync(barber.User.Id));
        Assert.Equal(PushNotificationKind.BarberBookingCreated, barberRow.Kind);
        Assert.Equal(bookingId, barberRow.BookingId);
        Assert.Equal(barber.User.Id, barberRow.UserId);
        Assert.Equal(PushDelivery.Skipped, barberRow.Delivery);
        Assert.Equal("no registered device", barberRow.LastError);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateBooking_AsAdminCustomer_DeliversTheCustomerCreatedEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Staff Admin Customer");

        var staffCustomer = await app.CreateUserAsync(
            $"push-staff-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(staffCustomer.Email.Value);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(staffCustomer.Id, DeviceToken);

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        var bookingId = await CreateBookingAsync(app, barber, startAt);

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Skipped);

        var row = Assert.Single(await push.GetNotificationsAsync(staffCustomer.Id));
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, row.Kind);
        Assert.Equal(bookingId, row.BookingId);
        Assert.Equal(staffCustomer.Id, row.UserId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);
        Assert.Equal("Salon Push Staff Admin Customer", row.SalonName);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(DeviceToken, captured.Token);
        Assert.Equal(
            "Вы записались: салон «Salon Push Staff Admin Customer»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, captured.Message.Type);

        var barberRow = Assert.Single(await push.GetNotificationsAsync(barber.User.Id));
        Assert.Equal(PushNotificationKind.BarberBookingCreated, barberRow.Kind);
        Assert.Equal(bookingId, barberRow.BookingId);
        Assert.Equal(barber.User.Id, barberRow.UserId);
        Assert.Equal(PushDelivery.Skipped, barberRow.Delivery);
        Assert.Equal("no registered device", barberRow.LastError);
    }

    [Fact(Timeout = 180_000)]
    public async Task Reminder_ForABarberCustomer_IsDelivered()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Staff Reminder Barber");

        var customer = await app.CreateUserAsync(
            $"push-staff-reminder-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        // Визит через 59 минут попадает в окно «за час», а created_at сдвигается в прошлое:
        // правило «бронь создана не позже start_at − lead» иначе отсекло бы свежую запись.
        var startAt = DateTimeOffset.UtcNow.AddMinutes(59);
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed);
        await data.SetBookingCreatedAtAsync(booking.Id, startAt.AddHours(-2));

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, DeviceToken);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);

        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushNotificationKind.Reminder, row.Kind);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(customer.Id, row.UserId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(DeviceToken, captured.Token);
        Assert.Equal(PushMessage.REMINDER_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task ConfirmBooking_ForAnAdminCustomer_DeliversTheCustomerConfirmedEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Staff Admin Confirm Barber");

        // Подтверждение — действие сотрудника, поэтому мастеру нужен claim профиля.
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(
            $"push-staff-admin-confirm-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Pending);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, DeviceToken);

        await app.AuthenticateAsync(barber.User.Email.Value);

        using (var confirmed = await app.Client.PostAsync(
            $"api/bookings/{booking.Id}/confirmation",
            content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        }

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Skipped);

        // Бронь засеяна напрямую, поэтому единственная строка клиента — событие подтверждения.
        var row = Assert.Single(await push.GetNotificationsAsync(customer.Id));
        Assert.Equal(PushNotificationKind.BookingConfirmed, row.Kind);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(customer.Id, row.UserId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(DeviceToken, captured.Token);
        Assert.Equal(PushMessage.BOOKING_CONFIRMED_TYPE, captured.Message.Type);
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
                $"push-staff-{Guid.NewGuid():N}",
                "integration test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Привязывает профиль барбера к пользователю: без claim'а <c>barber_profile_id</c>
    /// подтверждение записи отклоняется проверкой владения.
    /// </summary>
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
