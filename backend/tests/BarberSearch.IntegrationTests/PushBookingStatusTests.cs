using System.Net;
using System.Net.Http.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Bookings.CancelBooking;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Push;
using BarberSearch.Domain.Users;
using BarberSearch.Domain.WorkingSchedules;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Push;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// События статуса записи на реальной PostgreSQL через API: подтверждение, отмена и перенос
/// барбером, клиентские отмена и перенос с веба (виды 7 и 8) и гостевая бронь без получателя.
/// </summary>
/// <remarks>
/// Проверяется весь путь «переход статуса → строка очереди в том же запросе → доставка на
/// следующем тике»: строка читается из БД, а сообщение — из <see cref="CapturingPushSender"/>.
/// Бронь сидируется напрямую через <see cref="TestData"/>, поэтому в таблице нет строки
/// события создания и <c>Assert.Single</c> по получателю видит ровно событие статуса.
/// Уникальность тройки <c>(booking_id, kind, start_at)</c> позволяет повторным переносам
/// создавать новые строки, не задваивая одно и то же время.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushBookingStatusTests
{
    private static readonly TimeZoneInfo SalonZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly PostgresFixture _database;

    public PushBookingStatusTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Confirm_ThroughTheApi_EnqueuesTheConfirmationEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Confirm Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(CustomerEmail("confirm"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Pending);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

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

        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingConfirmed);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal("integration-device-token", captured.Token);
        Assert.Equal(
            "Ваша запись подтверждена: салон «Salon Push Status Confirm Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CONFIRMED_TYPE, captured.Message.Type);
        Assert.StartsWith(
            "Мастер Push Status Confirm Barber · Haircut Push Status Confirm Barber · ",
            captured.Message.Body,
            StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task BarberCancel_ThroughTheApi_EnqueuesTheCancellationEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Cancel Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(CustomerEmail("cancel"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(barber.User.Email.Value);

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest("Барбер заболел")))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Skipped);

        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingCancelledByBarber);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Ваша запись отменена: салон «Salon Push Status Cancel Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CANCELLED_TYPE, captured.Message.Type);

        // Причина отмены остаётся в брони и в текст push не попадает.
        Assert.DoesNotContain("Барбер заболел", captured.Message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Барбер заболел", captured.Message.Title, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task BarberReschedule_ThroughTheApi_EnqueuesTheNewStartTime()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Reschedule Barber");
        await LinkBarberProfileAsync(app, barber);
        await SeedWeekAsync(app, barber.Profile.Id);

        var customer = await app.CreateUserAsync(CustomerEmail("reschedule"));
        var date = TodayInSalon().AddDays(1);
        var originalStart = Local(date, 11);
        var targetStart = Local(date, 12);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            originalStart,
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(barber.User.Email.Value);

        using (var rescheduled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/reschedule",
            new RescheduleBookingRequest(targetStart)))
        {
            Assert.Equal(HttpStatusCode.NoContent, rescheduled.StatusCode);
        }

        var result = await app.RunPushTickAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Skipped);

        // Снимок строки — новое время визита, а не прежнее: по нему воркер отличает
        // устаревшее событие от актуального.
        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingRescheduledByBarber);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(targetStart, row.StartAt);
        Assert.Equal(PushDelivery.Sent, row.Delivery);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Ваша запись перенесена: салон «Salon Push Status Reschedule Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_RESCHEDULED_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CustomerCancel_WithoutSurfaceHeader_EnqueuesTheCustomerCancellationEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Self Cancel Barber");

        var customer = await app.CreateUserAsync(CustomerEmail("self-cancel"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(customer.Email.Value);

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        // Без заголовка X-Client-Surface запрос считается «другой поверхностью»: клиент
        // отменил запись с веба и получает push, а не письмо.
        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingCancelledByCustomer);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(customer.Id, row.UserId);

        // Мастер получает вторую сторону отмены: получатель — владелец профиля.
        var barberRow = Assert.Single(await push.GetNotificationsAsync(barber.User.Id));
        Assert.Equal(PushNotificationKind.BarberBookingCancelled, barberRow.Kind);
        Assert.Equal(booking.Id, barberRow.BookingId);
        Assert.Equal("Integration Test", barberRow.CustomerName);

        var result = await app.RunPushTickAsync();

        // Клиентское событие уходит на устройство, событие мастеру ждёт устройства барбера.
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Skipped);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Вы отменили запись: салон «Salon Push Status Self Cancel Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CANCELLED_BY_CUSTOMER_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CustomerReschedule_WithoutSurfaceHeader_EnqueuesTheCustomerRescheduleEvent()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Self Reschedule Barber");
        await SeedWeekAsync(app, barber.Profile.Id);

        var customer = await app.CreateUserAsync(CustomerEmail("self-reschedule"));
        var date = TodayInSalon().AddDays(1);
        var targetStart = Local(date, 12);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 11),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(customer.Email.Value);

        using (var rescheduled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/reschedule",
            new RescheduleBookingRequest(targetStart)))
        {
            Assert.Equal(HttpStatusCode.NoContent, rescheduled.StatusCode);
        }

        // Клиентский перенос с веба — такой же повод для push, как барберский: вид и тип
        // другие, а снимок — новое время визита.
        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingRescheduledByCustomer);
        Assert.Equal(booking.Id, row.BookingId);
        Assert.Equal(targetStart, row.StartAt);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Sent);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Вы перенесли запись: салон «Salon Push Status Self Reschedule Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_RESCHEDULED_BY_CUSTOMER_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task GuestBooking_EnqueuesNothing()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Status Guest Barber");
        await LinkBarberProfileAsync(app, barber);

        var booking = await data.CreateGuestBookingAsync(
            barber,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Pending);

        await app.AuthenticateAsync(barber.User.Email.Value);

        using (var confirmed = await app.Client.PostAsync(
            $"api/bookings/{booking.Id}/confirmation",
            content: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        }

        // У гостевой брони нет получателя: уведомитель возвращает успешный no-op, и строки
        // в очереди не появляются.
        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());

        var result = await app.RunPushTickAsync();

        Assert.Equal(PushTickResult.Empty, result);
        Assert.Empty(sender.Messages);
    }

    private static string CustomerEmail(string scenario)
        => $"push-status-{scenario}-{Guid.NewGuid():N}@barbersearch.test";

    /// <summary>
    /// Местное время салона (Europe/Berlin) как момент UTC.
    /// </summary>
    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var unspecified = date.ToDateTime(new TimeOnly(hour, minute));

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, SalonZone));
    }

    private static DateOnly TodayInSalon()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SalonZone).DateTime);

    /// <summary>
    /// Рабочая неделя барбера 10:00–18:00: без неё перенос не находит легального слота.
    /// </summary>
    private static async Task SeedWeekAsync(TestApp app, Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var link = await db.BarberSalons
            .Include(barberSalon => barberSalon.WorkingSchedules)
            .FirstAsync(barberSalon => barberSalon.BarberProfileId == barberProfileId);

        // CreateBookableBarberAsync уже задаёт круглосуточную неделю; замена, а не добавление,
        // иначе домен отклонит второй день недели как дубликат.
        var days = Enum.GetValues<DayOfWeek>()
            .Select(day => new WorkingDaySchedule(
                day,
                IsDayOff: false,
                new TimeOnly(10, 0),
                new TimeOnly(18, 0)))
            .ToList();

        Require(link.ReplaceWorkingSchedules(days));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Привязывает профиль барбера к пользователю: без claim'а <c>barber_profile_id</c>
    /// проверка владения отклонит подтверждение, отмену и перенос.
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
