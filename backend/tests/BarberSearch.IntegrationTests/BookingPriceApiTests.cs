using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Правка цены визита на реальной PostgreSQL: <c>204</c>, новая сумма во всех чтениях,
/// нетронутые статус/время/услуга, различимые <c>400</c>/<c>409</c>, права и строка
/// <c>booking.updated</c> с <c>changes.price</c>.
/// </summary>
/// <remarks>
/// Сценарии идут клиентским путём: брони готовятся <see cref="TestData"/>, а действие
/// выполняется HTTP-запросом, поэтому проверяются и маппинг ошибок, и запись аудита
/// в той же транзакции, что и сама правка.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BookingPriceApiTests
{
    private readonly PostgresFixture _database;

    public BookingPriceApiTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_changes_price_of_own_confirmed_visit()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Price Barber");
        await LinkBarberProfileAsync(app, barber);
        var customer = await app.CreateUserAsync(CustomerEmail());
        var startAt = FutureStart();

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(barber.User.Email);

        using (var response = await ChangePriceAsync(app.Client, booking.Id, 2000m))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Клиент видит новую сумму в «Предстоящих»: статус, время, услуга и салон не изменились.
        await app.AuthenticateAsync(customer.Email);
        var page = await GetUpcomingAsync(app.Client);
        var row = Assert.Single(page.Items, item => item.Id == booking.Id);

        Assert.Equal(2000m, row.Price);
        Assert.Equal(BookingStatus.Confirmed, row.Status);
        Assert.Equal(startAt, row.StartAt);
        Assert.Equal(startAt.AddMinutes(30), row.EndAt);
        Assert.Equal(barber.Service.Id, row.BarberServiceId);
        Assert.Equal(barber.Salon.Id, row.SalonId);
        Assert.Equal(barber.Profile.Id, row.BarberProfileId);

        // Кабинет барбера отдаёт ту же сумму.
        await app.AuthenticateAsync(barber.User.Email);
        var barberPage = await GetBarberBookingsAsync(app.Client);
        var barberRow = Assert.Single(barberPage.Items, item => item.Id == booking.Id);

        Assert.Equal(2000m, barberRow.Price);
        Assert.Equal(BookingStatus.Confirmed, barberRow.Status);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_changes_price_of_foreign_visit_from_the_card()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Admin Price Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        var startAt = FutureStart();

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed,
            price: 1500m);

        await CreateAdminAsync(app);

        using (var response = await ChangePriceAsync(app.Client, booking.Id, 2500m))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var card = await GetAdminBookingAsync(app.Client, booking.Id);

        Assert.Equal(2500m, card.Price);
        Assert.Equal(BookingStatus.Confirmed, card.Status);
        Assert.Equal(startAt, card.StartAt);
        Assert.Equal(barber.HairService.Name, card.Service.Name);
        Assert.Equal(barber.Salon.Id, card.Salon.Id);
    }

    [Fact(Timeout = 180_000)]
    public async Task Client_stranger_and_anonymous_cannot_change_price()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Guarded Price Barber");
        var stranger = await data.CreateBookableBarberAsync("Stranger Price Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        var startAt = FutureStart();

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed,
            price: 1500m);

        // Клиент брони — не владелец профиля: ручка общая для двух кабинетов.
        await app.AuthenticateAsync(customer.Email);
        using (var response = await ChangePriceAsync(app.Client, booking.Id, 2500m))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(
                "auth.forbidden",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        // Посторонний барбер видит ту же стену.
        await app.AuthenticateAsync(stranger.User.Email);
        using (var response = await ChangePriceAsync(app.Client, booking.Id, 2500m))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Аноним получает 401, а не 403: аутентификация проверяется до владения.
        using (var anonymous = app.CreateClient())
        using (var response = await ChangePriceAsync(anonymous, booking.Id, 2500m))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Для администратора неизвестный id — 404 с кодом брони.
        await CreateAdminAsync(app);
        using (var response = await ChangePriceAsync(app.Client, Guid.CreateVersion7(), 2500m))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(
                "\"errorCodes\":[\"booking.notFound\"]",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        // Ни одна из отказных попыток сумму не изменила.
        await app.AuthenticateAsync(customer.Email);
        var page = await GetUpcomingAsync(app.Client);
        Assert.Equal(1500m, Assert.Single(page.Items, item => item.Id == booking.Id).Price);
    }

    [Fact(Timeout = 180_000)]
    public async Task Validation_and_status_codes_are_distinguishable()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Validation Price Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        var startAt = FutureStart();

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Pending,
            price: 1500m);
        var cancelled = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt.AddHours(2),
            BookingStatus.Cancelled,
            price: 1500m);

        await CreateAdminAsync(app);

        await AssertPriceRejectedAsync(
            app.Client,
            booking.Id,
            -1m,
            HttpStatusCode.BadRequest,
            "booking.price.negative");
        await AssertPriceRejectedAsync(
            app.Client,
            booking.Id,
            100_000_000m,
            HttpStatusCode.BadRequest,
            "booking.price.tooLarge");
        await AssertPriceRejectedAsync(
            app.Client,
            booking.Id,
            1500.999m,
            HttpStatusCode.BadRequest,
            "booking.price.precision");

        // Отменённая запись не оплачивается: это 409 состояния, а не 400 значения.
        await AssertPriceRejectedAsync(
            app.Client,
            cancelled.Id,
            2000m,
            HttpStatusCode.Conflict,
            "booking.price.status");

        // Пропущенное поле нельзя толковать как ноль: ноль — валидная цена.
        using (var missing = await app.Client.PutAsJsonAsync(
                   $"api/bookings/{booking.Id}/price",
                   new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Contains("booking.price.required", await ReadErrorCodesAsync(missing));
        }

        // Границы диапазона принимаются.
        foreach (var accepted in new[] { 0m, 99_999_999.99m })
        {
            using var response = await ChangePriceAsync(app.Client, booking.Id, accepted);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Price_change_writes_one_audit_row_and_the_same_value_writes_none()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Price Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        var startAt = FutureStart();

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            startAt,
            BookingStatus.Confirmed,
            price: 1500m);

        await CreateAdminAsync(app);

        using (var response = await ChangePriceAsync(app.Client, booking.Id, 2000m))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var rows = await GetBookingAuditRowsAsync(app.Client, booking.Id);
        var row = Assert.Single(rows);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal("booking", row.EntityType);

        var price = Assert.Single(row.Changes, change => change.Field == "price");
        Assert.NotNull(price.From);
        Assert.NotNull(price.To);
        Assert.Equal(JsonValueKind.Number, price.From.Value.ValueKind);
        Assert.Equal(JsonValueKind.Number, price.To.Value.ValueKind);
        Assert.Equal(1500m, price.From.Value.GetDecimal());
        Assert.Equal(2000m, price.To.Value.GetDecimal());

        // Салон, услуга и барбер остаются вне журнала: эта правка их не касается.
        Assert.DoesNotContain(row.Changes, change => change.Field is "salonId" or "barberServiceId");

        // Повтор с той же суммой — успех без новой строки журнала.
        using (var repeated = await ChangePriceAsync(app.Client, booking.Id, 2000m))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }

        Assert.Single(await GetBookingAuditRowsAsync(app.Client, booking.Id));
    }

    private static string CustomerEmail() => $"price-{Guid.NewGuid():N}@barbersearch.test";

    /// <summary>
    /// Начало визита с точностью до секунды: <c>timestamptz</c> хранит микросекунды,
    /// поэтому тик-точное «сейчас» не переживает запись в базу и сравнение с ответом API.
    /// </summary>
    private static DateTimeOffset FutureStart()
    {
        var moment = DateTimeOffset.UtcNow.AddHours(2);

        return new DateTimeOffset(
            moment.Year,
            moment.Month,
            moment.Day,
            moment.Hour,
            moment.Minute,
            moment.Second,
            TimeSpan.Zero);
    }

    private static Task<HttpResponseMessage> ChangePriceAsync(
        HttpClient client,
        Guid bookingId,
        decimal price)
        => client.PutAsJsonAsync(
            $"api/bookings/{bookingId}/price",
            new { price });

    private static async Task AssertPriceRejectedAsync(
        HttpClient client,
        Guid bookingId,
        decimal price,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var response = await ChangePriceAsync(client, bookingId, price);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(expectedCode, await ReadErrorCodesAsync(response));
    }

    private static async Task<IReadOnlyList<string>> ReadErrorCodesAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!document.RootElement.TryGetProperty("errorCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Response {response.StatusCode} does not contain errorCodes.");
        }

        return codes.EnumerateArray()
            .Select(code => code.GetString() ?? string.Empty)
            .ToList();
    }

    private static async Task<PagedResponse<BookingResponse>> GetUpcomingAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/bookings?scope=upcoming");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BookingResponse>>(TestApp.JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    private static async Task<PagedResponse<BarberBookingResponse>> GetBarberBookingsAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(TestApp.JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    private static async Task<AdminBookingResponse> GetAdminBookingAsync(
        HttpClient client,
        Guid bookingId)
    {
        using var response = await client.GetAsync($"api/admin/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<AdminBookingResponse>(TestApp.JsonOptions);
        Assert.NotNull(card);

        return card;
    }

    /// <summary>
    /// Строки журнала по конкретной брони через админскую ленту: проверяется то, что
    /// записал <c>AuditTrail</c> и прочитал фильтр <c>entityType=booking</c>.
    /// </summary>
    private static async Task<IReadOnlyList<AdminAuditEntryResponse>> GetBookingAuditRowsAsync(
        HttpClient client,
        Guid bookingId)
    {
        using var response = await client.GetAsync("api/admin/audit?entityType=booking&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminAuditEntryResponse>>(TestApp.JsonOptions);
        Assert.NotNull(page);

        return page.Items.Where(row => row.EntityId == bookingId).ToList();
    }

    /// <summary>
    /// Привязывает профиль барбера к его аккаунту: без обратной ссылки в сессии нет claim'а
    /// <c>barber_profile_id</c>, и кабинет барбера отвечает <c>403</c>. Как в
    /// <c>BarberBookingReadTests</c>, где этот хелпер живёт локально.
    /// </summary>
    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
    }

    private static async Task<User> CreateAdminAsync(TestApp app)
    {
        var admin = await app.CreateUserAsync(
            $"price-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        await app.AuthenticateAsync(admin.Email);

        return admin;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }
}
