using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Скоупы списка броней клиента на реальной PostgreSQL (<c>GET api/bookings?scope=…</c>).
/// </summary>
/// <remarks>
/// Граница скоупа — <c>EndAt</c>, а не <c>StartAt</c>: активная бронь с прошедшим концом
/// уже история, а терминальный статус всегда история, даже если визит ещё впереди.
/// «Сейчас» берётся из <see cref="TimeProvider"/> хоста, поэтому тест сравнивает не
/// равенство с <c>now</c>, а смещения в час и сутки: точная граница — гонка с часами.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class CustomerBookingsScopeTests
{
    /// <summary>
    /// Сервер отдаёт перечисления строками, поэтому клиенту нужен конвертер.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public CustomerBookingsScopeTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Upcoming_returns_only_active_bookings_that_end_in_the_future()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var page = await GetPageAsync(app, "api/bookings?scope=upcoming");

        Assert.Equal(
            new[] { seed.UpcomingEarly.Id, seed.UpcomingMiddle.Id, seed.UpcomingLate.Id },
            page.Items.Select(item => item.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Past_returns_terminal_and_finished_bookings_newest_first()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var page = await GetPageAsync(app, "api/bookings?scope=past");

        // Отменённая бронь с будущим началом — тоже история: терминальный статус
        // важнее времени, иначе «Предстоящие» показали бы отменённый визит.
        Assert.Equal(
            new[]
            {
                seed.CancelledFuture.Id,
                seed.FinishedActive.Id,
                seed.CompletedPast.Id,
                seed.CancelledPast.Id,
                seed.NoShowPast.Id
            },
            page.Items.Select(item => item.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Without_scope_the_response_keeps_the_previous_descending_order()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var page = await GetPageAsync(app, "api/bookings");

        Assert.Equal(
            new[]
            {
                seed.CancelledFuture.Id,
                seed.UpcomingLate.Id,
                seed.UpcomingMiddle.Id,
                seed.UpcomingEarly.Id,
                seed.FinishedActive.Id,
                seed.CompletedPast.Id,
                seed.CancelledPast.Id,
                seed.NoShowPast.Id
            },
            page.Items.Select(item => item.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Explicit_all_scope_returns_the_same_default_list()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var defaultPage = await GetPageAsync(app, "api/bookings");
        var allPage = await GetPageAsync(app, "api/bookings?scope=all");

        Assert.Equal(
            defaultPage.Items.Select(item => item.Id),
            allPage.Items.Select(item => item.Id));
        Assert.Contains(allPage.Items, item => item.Id == seed.UpcomingEarly.Id);
        Assert.Contains(allPage.Items, item => item.Id == seed.NoShowPast.Id);
    }

    [Fact(Timeout = 180_000)]
    public async Task Upcoming_keyset_pages_visit_every_booking_once_and_move_forward()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var collected = await CollectAllAsync(
            app, "api/bookings?scope=upcoming&pageSize=2", pageSize: 2);

        Assert.Equal(
            new[] { seed.UpcomingEarly.Id, seed.UpcomingMiddle.Id, seed.UpcomingLate.Id },
            collected);
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    [Fact(Timeout = 180_000)]
    public async Task Past_keyset_pages_visit_every_booking_once_and_move_backward()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var collected = await CollectAllAsync(
            app, "api/bookings?scope=past&pageSize=2", pageSize: 2);

        // Порядок «истории» — свежие сверху, и курсор идёт в ту же сторону.
        Assert.Equal(
            new[]
            {
                seed.CancelledFuture.Id,
                seed.FinishedActive.Id,
                seed.CompletedPast.Id,
                seed.CancelledPast.Id,
                seed.NoShowPast.Id
            },
            collected);
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    [Fact(Timeout = 180_000)]
    public async Task Scope_is_trimmed_and_case_insensitive()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        var page = await GetPageAsync(app, "api/bookings?scope=%20Upcoming%20");

        Assert.Equal(
            new[] { seed.UpcomingEarly.Id, seed.UpcomingMiddle.Id, seed.UpcomingLate.Id },
            page.Items.Select(item => item.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Unknown_scope_is_rejected_with_the_scope_error_code()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        foreach (var scope in new[] { "wrong", "up", "0" })
        {
            using var response = await app.Client.GetAsync(
                $"api/bookings?scope={Uri.EscapeDataString(scope)}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "booking.getBookings.scope",
                await response.Content.ReadAsStringAsync());
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Foreign_user_id_without_admin_is_forbidden_with_any_scope()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        foreach (var scope in new[] { "all", "upcoming", "past" })
        {
            using var response = await app.Client.GetAsync(
                $"api/bookings?userId={seed.OtherCustomer.Id}&scope={scope}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(
                ApiErrorCodes.Auth.Forbidden,
                await response.Content.ReadAsStringAsync());
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_user_filter_combines_with_upcoming_scope()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Admin.Email);

        var page = await GetPageAsync(
            app,
            $"api/bookings?userId={seed.OtherCustomer.Id}&scope=upcoming");

        var only = Assert.Single(page.Items);
        Assert.Equal(seed.OtherCustomerUpcoming.Id, only.Id);
        Assert.Equal(seed.OtherCustomer.Id, only.UserId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Page_size_outside_bounds_is_rejected_for_every_scope()
    {
        await using var app = await TestApp.StartAsync(_database);
        var seed = await SeedAsync(app, new TestData(app));

        await app.AuthenticateAsync(seed.Customer.Email);

        foreach (var scope in new[] { "all", "upcoming", "past" })
        {
            using var tooSmall = await app.Client.GetAsync(
                $"api/bookings?scope={scope}&pageSize=0");
            using var tooLarge = await app.Client.GetAsync(
                $"api/bookings?scope={scope}&pageSize=101");

            Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            Assert.Contains(
                "booking.getBookings.pageSize",
                await tooSmall.Content.ReadAsStringAsync());
            Assert.Contains(
                "booking.getBookings.pageSize",
                await tooLarge.Content.ReadAsStringAsync());
        }
    }

    private static async Task<PagedResponse<BookingResponse>> GetPageAsync(
        TestApp app,
        string url)
    {
        using var response = await app.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<BookingResponse>>(
            JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    /// <summary>
    /// Сшивает страницы курсором, пока сервер не скажет, что дальше пусто.
    /// </summary>
    private static async Task<List<Guid>> CollectAllAsync(
        TestApp app,
        string url,
        int pageSize)
    {
        var collected = new List<Guid>();
        string? cursor = null;

        do
        {
            var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var next = cursor is null
                ? url
                : $"{url}{separator}cursor={Uri.EscapeDataString(cursor)}";

            var page = await GetPageAsync(app, next);

            Assert.True(
                page.Items.Count <= pageSize,
                $"pageSize={pageSize} должен ограничивать страницу");
            collected.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return collected;
    }

    /// <summary>
    /// Набор из трёх будущих и пяти прошедших броней плюс данные второго клиента и админа.
    /// </summary>
    /// <remarks>
    /// Статусы ведутся доменными переходами, а не SQL: тест не обходит правила агрегата.
    /// Времена не пересекаются у одного барбера — этого требует exclusion-ограничение.
    /// </remarks>
    private static async Task<SeededCustomerBookings> SeedAsync(TestApp app, TestData data)
    {
        var customer = await app.CreateUserAsync(
            $"booking-scope-customer-{Guid.NewGuid():N}@barbersearch.test");
        var otherCustomer = await app.CreateUserAsync(
            $"booking-scope-other-{Guid.NewGuid():N}@barbersearch.test");
        var admin = await app.CreateUserAsync(
            $"booking-scope-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var barber = await data.CreateBookableBarberAsync("Booking Scope Barber");
        var now = DateTimeOffset.UtcNow;

        // «Предстоящие»: активные, EndAt ещё впереди, ближайшая первой.
        var upcomingEarly = await SeedBookingAsync(
            app, barber, customer.Id, now.AddHours(1), BookingStatus.Pending);
        var upcomingMiddle = await SeedBookingAsync(
            app, barber, customer.Id, now.AddHours(3), BookingStatus.Confirmed);
        var upcomingLate = await SeedBookingAsync(
            app, barber, customer.Id, now.AddHours(5), BookingStatus.Pending);

        // «История»: прошедшая активная (граница — EndAt) и терминальные статусы.
        var finishedActive = await SeedBookingAsync(
            app, barber, customer.Id, now.AddHours(-2), BookingStatus.Confirmed);
        var completedPast = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(-3), BookingStatus.Completed);
        var cancelledPast = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(-4), BookingStatus.Cancelled);
        var noShowPast = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(-5), BookingStatus.NoShow);
        var cancelledFuture = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(3), BookingStatus.Cancelled);

        // Второй клиент на том же барбере: админский фильтр должен отсечь его строки.
        var otherCustomerUpcoming = await SeedBookingAsync(
            app, barber, otherCustomer.Id, now.AddHours(2), BookingStatus.Confirmed);
        var otherCustomerPast = await SeedBookingAsync(
            app, barber, otherCustomer.Id, now.AddDays(-6), BookingStatus.Cancelled);

        return new SeededCustomerBookings(
            customer,
            otherCustomer,
            admin,
            upcomingEarly,
            upcomingMiddle,
            upcomingLate,
            finishedActive,
            completedPast,
            cancelledPast,
            noShowPast,
            cancelledFuture,
            otherCustomerUpcoming,
            otherCustomerPast);
    }

    private static async Task<Booking> SeedBookingAsync(
        TestApp app,
        SeededBarber barber,
        Guid customerId,
        DateTimeOffset startAt,
        BookingStatus status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var booking = Require(Booking.Create(
            customerId,
            barber.Profile.Id,
            barber.Service.Id,
            barber.Salon.Id,
            startAt,
            startAt.AddMinutes(30),
            45m,
            $"booking-scope-{Guid.NewGuid():N}"));

        switch (status)
        {
            case BookingStatus.Confirmed:
                Require(booking.Confirm());
                break;
            case BookingStatus.Completed:
                Require(booking.Confirm());
                Require(booking.Complete());
                break;
            case BookingStatus.Cancelled:
                Require(booking.Cancel());
                break;
            case BookingStatus.NoShow:
                Require(booking.Confirm());
                Require(booking.MarkNoShow());
                break;
        }

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return booking;
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }

    /// <summary>
    /// Засеянные строки: тесты сравнивают идентификаторы и порядок, а не только статус ответа.
    /// </summary>
    private sealed record SeededCustomerBookings(
        User Customer,
        User OtherCustomer,
        User Admin,
        Booking UpcomingEarly,
        Booking UpcomingMiddle,
        Booking UpcomingLate,
        Booking FinishedActive,
        Booking CompletedPast,
        Booking CancelledPast,
        Booking NoShowPast,
        Booking CancelledFuture,
        Booking OtherCustomerUpcoming,
        Booking OtherCustomerPast);
}
