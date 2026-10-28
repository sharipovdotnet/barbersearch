using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админское чтение отзывов на реальной PostgreSQL: контекст визита, PII только для
/// администратора, фильтр по статусу и переход из очереди в опубликованные.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdminReviewReadTests
{
    private readonly PostgresFixture _database;

    public AdminReviewReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_reads_review_context_and_moves_it_to_published()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Admin Read Barber");
        var customer = await app.CreateUserAsync("admin-read-customer@barbersearch.test");
        var admin = await app.CreateUserAsync(
            "admin-read-admin@barbersearch.test",
            UserRole.Admin);

        var reviewId = await SeedReviewAsync(app, barber, customer.Id);

        await app.AuthenticateAsync(admin.Email);

        using var pending = await app.Client.GetAsync("api/admin/reviews?status=pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);

        var pendingPage = await pending.Content.ReadFromJsonAsync<PagedResponse<AdminReviewResponse>>();
        Assert.NotNull(pendingPage);

        var item = Assert.Single(pendingPage.Items);
        Assert.Equal(reviewId, item.Id);
        Assert.Equal(4, item.Rating);
        Assert.Equal("Пришёл на фейд, всё чётко.", item.Text);
        Assert.False(item.IsPublished);
        Assert.Equal("Admin Read Barber", item.Barber.DisplayName);
        Assert.Equal("Integration Test", item.Customer.FullName);
        Assert.Equal("admin-read-customer@barbersearch.test", item.Customer.Email);
        Assert.Equal("Haircut Admin Read Barber", item.Visit.ServiceName);
        Assert.Equal("Salon Admin Read Barber", item.Visit.SalonName);
        Assert.Equal("Europe/Berlin", item.Visit.TimeZoneId);

        using var detail = await app.Client.GetAsync($"api/admin/reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var detailBody = await detail.Content.ReadFromJsonAsync<AdminReviewResponse>();
        Assert.NotNull(detailBody);
        Assert.Equal(item.Customer.Email, detailBody.Customer.Email);

        using var publish = await app.Client.PostAsync(
            $"api/reviews/{reviewId}/publication",
            null);
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);

        using var published = await app.Client.GetAsync("api/admin/reviews?status=published");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var publishedPage = await published.Content.ReadFromJsonAsync<PagedResponse<AdminReviewResponse>>();
        Assert.NotNull(publishedPage);

        var publishedItem = Assert.Single(publishedPage.Items);
        Assert.Equal(reviewId, publishedItem.Id);
        Assert.True(publishedItem.IsPublished);
    }

    [Fact(Timeout = 120_000)]
    public async Task Admin_reviews_are_closed_for_anonymous_and_customers()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymous = await app.Client.GetAsync("api/admin/reviews");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var customer = await app.CreateUserAsync("admin-read-forbidden@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var forbidden = await app.Client.GetAsync("api/admin/reviews");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<Guid> SeedReviewAsync(
        TestApp app,
        SeededBarber barber,
        Guid customerId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var startAt = DateTimeOffset.UtcNow.AddDays(-2);
        var booking = Require(Booking.Create(
            customerId,
            barber.Profile.Id,
            barber.Service.Id,
            barber.Salon.Id,
            startAt,
            startAt.AddMinutes(30),
            45m,
            $"admin-read-{Guid.NewGuid():N}"));

        var review = Require(Review.Create(
            booking,
            customerId,
            4,
            "Пришёл на фейд, всё чётко."));

        db.Bookings.Add(booking);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        return review.Id;
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
}
