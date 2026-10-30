using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
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
/// Авторские отзывы на реальной PostgreSQL: свой список в обоих статусах, владение при удалении
/// и пересчёт сводки барбера при удалении опубликованного отзыва.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MyReviewTests
{
    private readonly PostgresFixture _database;

    public MyReviewTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task My_reviews_show_only_own_rows_in_both_statuses_with_visit_context()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("My Reviews Barber");
        var author = await app.CreateUserAsync("my-reviews-author@barbersearch.test");
        var stranger = await app.CreateUserAsync("my-reviews-stranger@barbersearch.test");

        var published = await SeedReviewAsync(app, barber, author.Id, 5, "Published one", published: true);

        // Разные created_at: свежие сверху, поэтому скрытый отзыв создаётся последним.
        await Task.Delay(20);

        var hidden = await SeedReviewAsync(app, barber, author.Id, 3, "Waiting for moderation");
        var foreign = await SeedReviewAsync(app, barber, stranger.Id, 4, "Not mine", published: true);

        using var anonymous = await app.Client.GetAsync("api/reviews/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await app.AuthenticateAsync(author.Email);

        using var mine = await app.Client.GetAsync("api/reviews/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var page = await mine.Content.ReadFromJsonAsync<PagedResponse<MyReviewResponse>>();
        Assert.NotNull(page);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(hidden, page.Items[0].Id);
        Assert.Equal(published, page.Items[1].Id);
        Assert.DoesNotContain(page.Items, item => item.Id == foreign);
        Assert.Null(page.NextCursor);

        // Статус — единственное различие: скрытый отзыв виден автору, но не опубликован.
        Assert.False(page.Items[0].IsPublished);
        Assert.True(page.Items[1].IsPublished);

        var first = page.Items[0];
        Assert.Equal("Waiting for moderation", first.Text);
        Assert.Equal("My Reviews Barber", first.Barber.DisplayName);
        Assert.Equal(barber.Profile.Id, first.Barber.Id);
        Assert.Equal("Haircut My Reviews Barber", first.Visit.ServiceName);
        Assert.Equal("Salon My Reviews Barber", first.Visit.SalonName);
        Assert.Equal("Europe/Berlin", first.Visit.TimeZoneId);
        Assert.Equal(45m, first.Visit.Price);
        Assert.NotEqual(Guid.Empty, first.Visit.BookingId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Deleting_a_review_is_author_only_and_idempotent_as_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Delete Review Barber");
        var author = await app.CreateUserAsync("delete-review-author@barbersearch.test");
        var stranger = await app.CreateUserAsync("delete-review-stranger@barbersearch.test");

        var reviewId = await SeedReviewAsync(app, barber, author.Id, 4, "To be deleted");
        var foreignId = await SeedReviewAsync(app, barber, stranger.Id, 2, "Keep me");

        await app.AuthenticateAsync(stranger.Email);

        using var forbidden = await app.Client.DeleteAsync($"api/reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("auth.forbidden", await forbidden.Content.ReadAsStringAsync());
        Assert.True(await ReviewExistsAsync(app, reviewId));

        await app.AuthenticateAsync(author.Email);

        using var deleted = await app.Client.DeleteAsync($"api/reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(await ReviewExistsAsync(app, reviewId));

        // Повторное удаление — 404, как и неизвестный id: UI трактует это как «уже удалён».
        using var repeated = await app.Client.DeleteAsync($"api/reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);

        using var unknown = await app.Client.DeleteAsync($"api/reviews/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        Assert.True(await ReviewExistsAsync(app, foreignId));
    }

    [Fact(Timeout = 180_000)]
    public async Task Deleting_a_published_review_recalculates_barber_rating()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Rating Recalc Barber");
        var author = await app.CreateUserAsync("rating-recalc-author@barbersearch.test");
        var admin = await app.CreateUserAsync(
            "rating-recalc-admin@barbersearch.test",
            UserRole.Admin);

        var reviewId = await SeedReviewAsync(app, barber, author.Id, 4, "Counted in the rating");

        await app.AuthenticateAsync(admin.Email);

        using var published = await app.Client.PostAsync(
            $"api/reviews/{reviewId}/publication",
            null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        var (rating, count) = await ReadRatingAsync(app, barber.Profile.Id);
        Assert.Equal(4.00m, rating);
        Assert.Equal(1, count);

        await app.AuthenticateAsync(author.Email);

        using var deleted = await app.Client.DeleteAsync($"api/reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Последний опубликованный отзыв удалён: сводка очищается, а не остаётся с ним.
        var summaryAfterDelete = await ReadRatingAsync(app, barber.Profile.Id);
        Assert.Null(summaryAfterDelete.Rating);
        Assert.Equal(0, summaryAfterDelete.Count);
    }

    [Fact(Timeout = 120_000)]
    public async Task Mine_rejects_broken_page_size_and_cursor()
    {
        await using var app = await TestApp.StartAsync(_database);
        var author = await app.CreateUserAsync("mine-errors@barbersearch.test");
        await app.AuthenticateAsync(author.Email);

        using var pageSize = await app.Client.GetAsync("api/reviews/mine?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, pageSize.StatusCode);
        Assert.Contains(
            "review.getMyReviews.pageSize",
            await pageSize.Content.ReadAsStringAsync());

        using var cursor = await app.Client.GetAsync("api/reviews/mine?cursor=broken");
        Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
        Assert.Contains(
            "review.cursor.invalid",
            await cursor.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> SeedReviewAsync(
        TestApp app,
        SeededBarber barber,
        Guid customerId,
        int rating,
        string text,
        bool published = false)
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
            $"my-reviews-{Guid.NewGuid():N}"));

        // Завершённая бронь: отзыв пишется по состоявшемуся визиту, и exclusion-ограничение
        // не мешает засеять несколько визитов одного барбера на одно и то же время.
        Require(booking.Confirm());
        Require(booking.Complete());

        var review = Require(Review.Create(booking, customerId, rating, text));

        // R-35: Create оставляет отзыв скрытым; опубликованные для тестов включаем явно.
        if (published)
        {
            Require(review.Publish());
        }

        db.Bookings.Add(booking);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        return review.Id;
    }

    private static async Task<bool> ReviewExistsAsync(TestApp app, Guid reviewId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Reviews.AsNoTracking().AnyAsync(review => review.Id == reviewId);
    }

    private static async Task<(decimal? Rating, int Count)> ReadRatingAsync(
        TestApp app,
        Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.BarberProfiles
            .AsNoTracking()
            .Where(profile => profile.Id == barberProfileId)
            .Select(profile => new { profile.Rating, profile.ReviewsCount })
            .SingleAsync();

        return (row.Rating, row.ReviewsCount);
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
