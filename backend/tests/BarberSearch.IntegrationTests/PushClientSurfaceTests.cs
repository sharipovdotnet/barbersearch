using System.Net;
using System.Net.Http.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Bookings.CancelBooking;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.EmailOutbox;
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
/// Признак поверхности запроса (<c>X-Client-Surface</c>) на реальной PostgreSQL: клиентские
/// создание, отмена и перенос из приложения не создают push-строку, действия с веба и сотрудника
/// создают, а неизвестное значение заголовка не является ошибкой запроса.
/// </summary>
/// <remarks>
/// Проверяется весь путь «HTTP → обработчик → уведомитель → строка очереди»: строки читаются из
/// БД, тип в <c>data</c> — из сообщения <see cref="CapturingPushSender"/>. Отдельный сценарий
/// проверяет обратную сторону гейта: отмена из приложения ставит письмо в <c>email_outbox</c>,
/// и повтор запроса не добавляет вторую строку. Гостевые и выключенные сценарии живут в
/// <c>PushBookingCreatedTests</c> и <c>PushBookingStatusTests</c>.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PushClientSurfaceTests
{
    private const string SurfaceHeader = "X-Client-Surface";

    private readonly PostgresFixture _database;

    public PushClientSurfaceTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task CancelFromApp_WithSurfaceHeader_CreatesNoPushRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Cancel App Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("cancel-app"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);
        app.Client.DefaultRequestHeaders.Add(SurfaceHeader, "app");

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        // Действие в том же приложении, которое получило бы push: строка не создаётся.
        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());

        var result = await app.RunPushTickAsync();

        Assert.Equal(PushTickResult.Empty, result);
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task CancelFromWeb_WithoutHeader_CreatesCustomerCancelledRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Cancel Web Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("cancel-web"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        await app.AuthenticateAsync(customer.Email);

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingCancelledByCustomer);
        Assert.Equal(booking.Id, row.BookingId);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Sent);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Вы отменили запись: салон «Salon Push Surface Cancel Web Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CANCELLED_BY_CUSTOMER_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CancelWithUnknownSurfaceValue_CreatesCustomerRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Unknown Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("cancel-unknown"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);
        app.Client.DefaultRequestHeaders.Add(SurfaceHeader, "AppX");

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            // Неизвестное значение — «другая поверхность», а не ошибка запроса.
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        var push = new PushTestData(app);
        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingCancelledByCustomer);
        Assert.Equal(booking.Id, row.BookingId);
    }

    [Fact(Timeout = 180_000)]
    public async Task AdminCancel_CreatesBarberCancelledRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Admin Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("admin-cancel"));
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customer.Id, "integration-device-token");

        var adminEmail = $"push-surface-admin-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(adminEmail, UserRole.Admin);
        await app.AuthenticateAsync(adminEmail);

        // Регрессия: даже с заголовком app действие администратора уведомляет клиента.
        app.Client.DefaultRequestHeaders.Add(SurfaceHeader, "app");

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        var row = Assert.Single(
            await push.GetNotificationsAsync(customer.Id),
            notification => notification.Kind == PushNotificationKind.BookingCancelledByBarber);
        Assert.Equal(booking.Id, row.BookingId);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Sent);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Ваша запись отменена: салон «Salon Push Surface Admin Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CANCELLED_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateFromApp_WithSurfaceHeader_CreatesNoPushRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Create App Barber");

        var email = CustomerEmail("create-app");
        await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);
        app.Client.DefaultRequestHeaders.Add(SurfaceHeader, "app");

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        await CreateBookingAsync(app, barber, startAt);

        // Самозапись из приложения: экран уже подтвердил бронь, push не создаётся.
        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());

        var result = await app.RunPushTickAsync();

        Assert.Equal(PushTickResult.Empty, result);
        Assert.Empty(sender.Messages);
    }

    [Fact(Timeout = 180_000)]
    public async Task CreateFromWeb_WithoutHeader_CreatesCustomerCreatedRow()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Create Web Barber");

        var email = CustomerEmail("create-web");
        var customerId = await app.RegisterAsync(email);
        await app.AuthenticateAsync(email);

        var push = new PushTestData(app);
        await push.SeedDeviceTokenAsync(customerId, "integration-device-token");

        var startAt = DateTimeOffset.UtcNow.AddDays(2);
        var bookingId = await CreateBookingAsync(app, barber, startAt);

        var row = Assert.Single(await push.GetNotificationsAsync(customerId));
        Assert.Equal(PushNotificationKind.BookingCreatedByCustomer, row.Kind);
        Assert.Equal(bookingId, row.BookingId);

        var result = await app.RunPushTickAsync();

        Assert.Equal(1, result.Sent);

        var captured = Assert.Single(sender.Messages);
        Assert.Equal(
            "Вы записались: салон «Salon Push Surface Create Web Barber»",
            captured.Message.Title);
        Assert.Equal(PushMessage.BOOKING_CREATED_TYPE, captured.Message.Type);
    }

    [Fact(Timeout = 180_000)]
    public async Task CustomerCancelFromApp_QueuesCancellationEmailOnce()
    {
        var sender = new CapturingPushSender();

        await using var app = await TestApp.StartAsync(
            _database,
            PushEnabled(),
            pushSender: sender);

        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Push Surface Email Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("cancel-email"));
        await MarkEmailVerifiedAsync(app, customer.Id);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(2),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);
        app.Client.DefaultRequestHeaders.Add(SurfaceHeader, "app");

        using (var cancelled = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        // Отмена из приложения не создаёт push, но ставит письмо на подтверждённый адрес.
        var push = new PushTestData(app);
        Assert.Empty(await push.GetAllNotificationsAsync());

        var first = Assert.Single(await GetOutboxAsync(app));
        Assert.Equal(customer.Email, first.ToEmail);
        Assert.Equal(
            "Запись отменена — Салон «Salon Push Surface Email Barber»",
            first.Subject);
        Assert.Equal($"booking:{booking.Id:D}:cancelled", first.DeduplicationKey);

        // Повтор того же запроса уже не проходит доменную проверку статуса, но и он не должен
        // добавить вторую строку: письмо одной отмены ставится ровно один раз.
        using (var retry = await app.Client.PostAsJsonAsync(
            $"api/bookings/{booking.Id}/cancellation",
            new CancelBookingRequest(null)))
        {
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        }

        Assert.Single(await GetOutboxAsync(app));
    }

    private static string CustomerEmail(string scenario)
        => $"push-surface-{scenario}-{Guid.NewGuid():N}@barbersearch.test";

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
                $"push-surface-{Guid.NewGuid():N}",
                "integration test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Подтверждает email пользователя до запроса: письмо уходит только на подтверждённый адрес.
    /// </summary>
    private static async Task MarkEmailVerifiedAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == userId);
        Require(user.MarkEmailAsVerified());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Строки <c>email_outbox</c> в порядке постановки в очередь.
    /// </summary>
    private static async Task<IReadOnlyList<EmailOutboxMessage>> GetOutboxAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.EmailOutbox
            .AsNoTracking()
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
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
