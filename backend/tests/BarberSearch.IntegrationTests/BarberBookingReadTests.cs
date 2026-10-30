using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Bookings.Barber;
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
/// Записи барбера на реальной PostgreSQL: права, изоляция профилей, скоупы, PII и ошибки.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberBookingReadTests
{
    /// <summary>
    /// Сервер отдаёт перечисления строками, поэтому клиенту нужен конвертер.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberBookingReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_customer_and_admin_cannot_read_barber_bookings()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync($"barber-books-customer-{Guid.NewGuid():N}@barbersearch.test");
        var admin = await app.CreateUserAsync(
            $"barber-books-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        using var anonymous = await app.Client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await app.AuthenticateAsync(customer.Email);

        using var asCustomer = await app.Client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);

        await app.AuthenticateAsync(admin.Email);

        using var asAdmin = await app.Client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_reads_only_own_profile_bookings_in_both_scopes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("My Bookings Barber");
        var otherBarber = await data.CreateBookableBarberAsync("Other Bookings Barber");
        await LinkBarberProfileAsync(app, barber);
        await LinkBarberProfileAsync(app, otherBarber);

        var customer = await app.CreateUserAsync(
            $"barber-books-client-{Guid.NewGuid():N}@barbersearch.test");
        await SetCustomerProfileAsync(app, customer.Id, "Иван", "Петров", "+7 999 123-45-67");

        var now = DateTimeOffset.UtcNow;

        var pending = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(2), BookingStatus.Pending);
        var confirmed = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(2).AddHours(2), BookingStatus.Confirmed);
        var completed = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(-3), BookingStatus.Completed);
        var cancelled = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(-4), BookingStatus.Cancelled);
        var foreign = await SeedBookingAsync(
            app, otherBarber, customer.Id, now.AddDays(3), BookingStatus.Confirmed);

        await app.AuthenticateAsync(barber.User.Email);

        using var upcoming = await app.Client.GetAsync("api/barber/bookings");
        Assert.Equal(HttpStatusCode.OK, upcoming.StatusCode);

        var upcomingBody = await upcoming.Content.ReadAsStringAsync();
        var upcomingPage = JsonSerializer.Deserialize<PagedResponse<BarberBookingResponse>>(
            upcomingBody,
            JsonOptions);
        Assert.NotNull(upcomingPage);
        Assert.Equal(new[] { pending.Id, confirmed.Id }, upcomingPage.Items.Select(item => item.Id));
        Assert.DoesNotContain(upcomingPage.Items, item => item.Id == foreign.Id);

        var first = upcomingPage.Items[0];
        Assert.Equal(BookingStatus.Pending, first.Status);
        Assert.Equal("Иван Петров", first.Customer.DisplayName);
        Assert.Equal("+7 999 123-45-67", first.Customer.PhoneNumber);
        Assert.Equal("Haircut My Bookings Barber", first.Service.Name);
        Assert.Equal("Salon My Bookings Barber", first.Salon.Name);
        Assert.Equal("Teststrasse 1", first.Salon.Address);
        Assert.Equal("Berlin", first.Salon.City);
        Assert.Equal("Europe/Berlin", first.Salon.TimeZoneId);
        Assert.Equal(45m, first.Price);

        // PII ограничена именем и телефоном: email и идентификаторы связей не выходят наружу.
        Assert.DoesNotContain(customer.Email, upcomingBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", upcomingBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("barberProfileId", upcomingBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotencyKey", upcomingBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt", upcomingBody, StringComparison.OrdinalIgnoreCase);

        using var past = await app.Client.GetAsync("api/barber/bookings?scope=past");

        var pastPage = await past.Content.ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
        Assert.NotNull(pastPage);
        Assert.Equal(new[] { completed.Id, cancelled.Id }, pastPage.Items.Select(item => item.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_bookings_paginate_with_cursor_and_reject_bad_parameters()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Paging Barber");
        await LinkBarberProfileAsync(app, barber);
        var customer = await app.CreateUserAsync(
            $"barber-books-paging-{Guid.NewGuid():N}@barbersearch.test");

        var now = DateTimeOffset.UtcNow;
        var first = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(2), BookingStatus.Confirmed);
        var second = await SeedBookingAsync(
            app, barber, customer.Id, now.AddDays(2).AddHours(2), BookingStatus.Pending);

        await app.AuthenticateAsync(barber.User.Email);

        using var pageSizeOne = await app.Client.GetAsync("api/barber/bookings?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pageSizeOne.StatusCode);

        var page = await pageSizeOne.Content
            .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal(first.Id, item.Id);
        Assert.NotNull(page.NextCursor);

        using var secondPage = await app.Client.GetAsync(
            $"api/barber/bookings?pageSize=1&cursor={Uri.EscapeDataString(page.NextCursor)}");

        var next = await secondPage.Content
            .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
        Assert.NotNull(next);
        var secondItem = Assert.Single(next.Items);
        Assert.Equal(second.Id, secondItem.Id);
        Assert.Null(next.NextCursor);

        using var badScope = await app.Client.GetAsync("api/barber/bookings?scope=wat");
        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
        Assert.Contains(
            "booking.getBarberBookings.scope",
            await badScope.Content.ReadAsStringAsync());

        using var badPageSize = await app.Client.GetAsync("api/barber/bookings?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, badPageSize.StatusCode);
        Assert.Contains(
            "booking.getBarberBookings.pageSize",
            await badPageSize.Content.ReadAsStringAsync());

        using var badCursor = await app.Client.GetAsync("api/barber/bookings?cursor=broken");
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        Assert.Contains("booking.cursor.invalid", await badCursor.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Barber_without_linked_profile_gets_forbidden()
    {
        await using var app = await TestApp.StartAsync(_database);
        var unlinked = await app.CreateUserAsync(
            $"barber-books-unlinked-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await app.AuthenticateAsync(unlinked.Email);

        using var response = await app.Client.GetAsync("api/barber/bookings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync());
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
    }

    private static async Task SetCustomerProfileAsync(
        TestApp app,
        Guid userId,
        string firstName,
        string lastName,
        string phoneNumber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == userId);
        Require(user.UpdateProfile(firstName, lastName, phoneNumber, avatarUrl: null));
        await db.SaveChangesAsync();
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
            $"barber-books-{Guid.NewGuid():N}"));

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
}
