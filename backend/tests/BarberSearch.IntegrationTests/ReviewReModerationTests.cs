using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Пре-модерация не обходится правкой: опубликованный отзыв с новым текстом возвращается
/// в очередь модерации и снова исчезает из публичного списка до решения администратора (R-35).
/// </summary>
/// <remarks>
/// Сценарий идёт через HTTP: правило живёт в домене, но обещание клиенту — это код `200`
/// и новое поле <c>isPublished</c> в ответе, а очереди — первая строка `GET api/admin/reviews`.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ReviewReModerationTests
{
    private const string PENDING_ENDPOINT = "api/admin/reviews?status=pending&pageSize=100";
    private const string PUBLISHED_ENDPOINT = "api/admin/reviews?status=published&pageSize=100";

    private readonly PostgresFixture _database;

    public ReviewReModerationTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Editing_published_text_returns_the_review_to_the_queue()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Re-moderation Barber");
        var customer = await app.CreateUserAsync(
            $"review-remod-{Guid.NewGuid():N}@barbersearch.test");
        var admin = await app.CreateUserAsync(
            $"review-remod-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);

        var reviewId = await data.CreateReviewAsync(
            barber,
            customer.Id,
            rating: 4,
            published: true);

        await using (var session = await app.SignInWithCookieAsync(customer.Email))
        {
            using var response = await UpdateAsync(
                session,
                reviewId,
                new { rating = 4, text = "Текст после одобрения." },
                expectedStatus: HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<UpdateReviewResponse>();
            Assert.NotNull(body);
            Assert.False(body.IsPublished);
        }

        await app.AuthenticateAsync(admin.Email);

        var pending = await ReadPageAsync(app, PENDING_ENDPOINT);
        var queued = Assert.Single(pending.Items, item => item.Id == reviewId);
        Assert.False(queued.IsPublished);
        Assert.Equal("Текст после одобрения.", queued.Text);

        // Строка вернулась в конец очереди: ожидание считается от правки, а не от создания.
        Assert.Equal(reviewId, pending.Items[^1].Id);

        var published = await ReadPageAsync(app, PUBLISHED_ENDPOINT);
        Assert.DoesNotContain(published.Items, item => item.Id == reviewId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Editing_only_the_rating_keeps_the_review_published()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Rating Only Barber");
        var customer = await app.CreateUserAsync(
            $"review-rating-only-{Guid.NewGuid():N}@barbersearch.test");

        var reviewId = await data.CreateReviewAsync(
            barber,
            customer.Id,
            rating: 4,
            published: true);

        await using var session = await app.SignInWithCookieAsync(customer.Email);

        // Текст тот же, что в сиде, меняется только оценка: повторная модерация не нужна.
        using var response = await UpdateAsync(
            session,
            reviewId,
            new { rating = 2, text = "Seeded review" },
            expectedStatus: HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UpdateReviewResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsPublished);
    }

    private static async Task<HttpResponseMessage> UpdateAsync(
        CookieSession session,
        Guid reviewId,
        object payload,
        HttpStatusCode expectedStatus)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/reviews/{reviewId}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(AuthConstants.CSRF_HEADER_NAME, session.CsrfToken);

        var response = await session.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);

        return response;
    }

    private static async Task<PagedResponse<AdminReviewResponse>> ReadPageAsync(
        TestApp app,
        string endpoint)
    {
        using var response = await app.Client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<AdminReviewResponse>>();
        Assert.NotNull(page);

        return page;
    }
}
