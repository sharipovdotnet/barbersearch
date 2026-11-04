using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CancelBooking;
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
/// Отмена записи барбером: без причины заявка не отменяется, причина сохраняется
/// и возвращается клиенту в истории посещений.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberCancellationReasonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberCancellationReasonTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Barber_cancel_requires_reason_and_customer_sees_it_in_history()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Cancellation Reason Barber");
        await LinkBarberProfileAsync(app, barber);
        var customer = await app.CreateUserAsync(
            $"cancel-reason-{Guid.NewGuid():N}@barbersearch.test");
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(10));

        await app.AuthenticateAsync(barber.User.Email);

        using (var noReason = await app.Client.PostAsync(
                   $"api/bookings/{booking.Id}/cancellation",
                   null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Contains(
                "booking.cancel.reasonRequired",
                await noReason.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        await AssertStatusAsync(app, booking.Id, BookingStatus.Pending, reason: null);

        using (var tooLong = await app.Client.PostAsJsonAsync(
                   $"api/bookings/{booking.Id}/cancellation",
                   new CancelBookingRequest(new string('a', 501))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains(
                "booking.cancel.reasonTooLong",
                await tooLong.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        await AssertStatusAsync(app, booking.Id, BookingStatus.Pending, reason: null);

        using (var cancelled = await app.Client.PostAsJsonAsync(
                   $"api/bookings/{booking.Id}/cancellation",
                   new CancelBookingRequest("  Барбер заболел  ")))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        await AssertStatusAsync(app, booking.Id, BookingStatus.Cancelled, "Барбер заболел");

        using (var barberHistory = await app.Client.GetAsync("api/barber/bookings?scope=past"))
        {
            Assert.Equal(HttpStatusCode.OK, barberHistory.StatusCode);

            var page = await barberHistory.Content
                .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(JsonOptions);
            Assert.NotNull(page);

            var item = Assert.Single(page.Items, candidate => candidate.Id == booking.Id);
            Assert.Equal(BookingStatus.Cancelled, item.Status);
            Assert.Equal("Барбер заболел", item.CancellationReason);
        }

        await app.AuthenticateAsync(customer.Email);

        using (var history = await app.Client.GetAsync("api/bookings?scope=past"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);

            var page = await history.Content
                .ReadFromJsonAsync<PagedResponse<BookingResponse>>(JsonOptions);
            Assert.NotNull(page);

            var item = Assert.Single(page.Items, candidate => candidate.Id == booking.Id);
            Assert.Equal(BookingStatus.Cancelled, item.Status);
            Assert.Equal("Барбер заболел", item.CancellationReason);
        }

        var auditRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "booking"
                && candidate.EntityId == booking.Id);

        Assert.Contains("cancellationReason", auditRow.Payload, StringComparison.Ordinal);
        Assert.Contains("Барбер заболел", auditRow.Payload, StringComparison.Ordinal);
    }

    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);
        var linked = user.LinkBarberProfile(barber.Profile.Id);

        if (linked.IsFailure)
        {
            throw new InvalidOperationException(
                string.Join("; ", linked.Error.Errors.Select(error => error.Code)));
        }

        await db.SaveChangesAsync();
    }

    private static async Task AssertStatusAsync(
        TestApp app,
        Guid bookingId,
        BookingStatus status,
        string? reason)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Bookings
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == bookingId);

        Assert.Equal(status, booking.Status);
        Assert.Equal(reason, booking.CancellationReason);
    }
}
