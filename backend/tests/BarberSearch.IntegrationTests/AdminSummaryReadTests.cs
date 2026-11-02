using System.Net;
using System.Text.Json;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Админская сводка (<c>GET api/admin/summary</c>) на реальной PostgreSQL: доступ, счётчики
/// из тех же предикатов, что фильтры разделов, «самый старый» отзыв как первая строка
/// FIFO-очереди, продвижение очереди после публикации и отсутствие email клиента в теле.
/// </summary>
/// <remarks>
/// <para>
/// База общая на прогон и очищается между тестами, поэтому счётчики проверяются дельтами,
/// а не абсолютными числами: у сводки нет параметров, которые сузили бы выборку.
/// </para>
/// <para>
/// DTO чтения объявлены локально: тест проверяет контракт и должен компилироваться до того,
/// как появятся production-контракты. Отсутствие <c>total</c>/<c>nextCursor</c> проверяется
/// по сырому телу, потому что локальная запись о них просто ничего не знает.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AdminSummaryReadTests
{
    private const string ENDPOINT = "api/admin/summary";

    private const string REVIEWS_ENDPOINT = "api/admin/reviews";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgresFixture _database;

    public AdminSummaryReadTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Anonymous_gets_401_and_non_admin_gets_403()
    {
        await using var app = await TestApp.StartAsync(_database);

        using (var anonymous = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var customer = await app.CreateUserAsync(
            $"admin-summary-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using (var forbidden = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var barber = await app.CreateUserAsync(
            $"admin-summary-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using (var forbidden = await app.Client.GetAsync(ENDPOINT))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var admin = await app.CreateUserAsync(
            $"admin-summary-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var response = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await ReadSummaryAsync(response);

        Assert.NotNull(summary.Reviews);
        Assert.NotNull(summary.Barbers);
        Assert.NotNull(summary.Salons);
    }

    [Fact(Timeout = 180_000)]
    public async Task Counts_follow_the_admin_sections()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var reviewBarber = await data.CreateBookableBarberAsync("Summary Review Barber");
        var publishedBarber = await data.CreateBookableBarberAsync("Summary Published Barber");
        var customer = await app.CreateUserAsync(
            $"admin-summary-counts-{Guid.NewGuid():N}@barbersearch.test");

        var admin = await app.CreateUserAsync(
            $"admin-summary-counts-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var before = await GetSummaryAsync(app);

        // Очередь модерации — ровно неопубликованный отзыв; опубликованный ею не становится.
        await data.CreateReviewAsync(reviewBarber, customer.Id, published: false);
        await data.CreateReviewAsync(publishedBarber, customer.Id, published: true);

        // Профиль без подтверждения — ровно `AdminBarberStatus.Pending`, без условия `is_active`.
        var pendingBarber = await data.CreateBarberProfileAsync("Summary Pending Barber");
        await data.SetBarberProfileStateAsync(pendingBarber.Id, isVerified: false, isActive: true);

        // Выключенный салон — ровно `AdminCatalogueStatus.Hidden`.
        await data.CreateSalonAsync("Summary Hidden Salon", isActive: false);

        var after = await GetSummaryAsync(app);

        Assert.Equal(before.Reviews.PendingCount + 1, after.Reviews.PendingCount);
        Assert.Equal(before.Barbers.UnverifiedCount + 1, after.Barbers.UnverifiedCount);
        Assert.Equal(before.Salons.InactiveCount + 1, after.Salons.InactiveCount);

        // Опубликованный отзыв счётчик не двигает: вкладка «На модерации» его не показывает.
        var queued = await GetQueueIdsAsync(app);
        Assert.All(queued, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(after.Reviews.PendingCount, queued.Count);
    }

    [Fact(Timeout = 180_000)]
    public async Task Oldest_review_is_the_first_row_of_the_moderation_queue()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var firstBarber = await data.CreateBookableBarberAsync("Summary Oldest First");
        var secondBarber = await data.CreateBookableBarberAsync("Summary Oldest Second");
        var customer = await app.CreateUserAsync(
            $"admin-summary-oldest-{Guid.NewGuid():N}@barbersearch.test");

        var admin = await app.CreateUserAsync(
            $"admin-summary-oldest-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        // Два отзыва у разных барберов: у одного барбера фиксированное время визита,
        // и две брони нарушили бы exclusion-ограничение.
        var firstReviewId = await data.CreateReviewAsync(firstBarber, customer.Id, rating: 4);
        var secondReviewId = await data.CreateReviewAsync(secondBarber, customer.Id, rating: 5);

        var summary = await GetSummaryAsync(app);
        var queueIds = await GetQueueIdsAsync(app);

        Assert.NotNull(summary.Reviews.Oldest);
        Assert.Equal(queueIds[0], summary.Reviews.Oldest.Id);
        Assert.Contains(summary.Reviews.Oldest.Id, new[] { firstReviewId, secondReviewId });
    }

    [Fact(Timeout = 180_000)]
    public async Task Publishing_the_oldest_review_advances_the_summary()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var firstBarber = await data.CreateBookableBarberAsync("Summary Publish First");
        var secondBarber = await data.CreateBookableBarberAsync("Summary Publish Second");
        var customer = await app.CreateUserAsync(
            $"admin-summary-publish-{Guid.NewGuid():N}@barbersearch.test");

        await data.CreateReviewAsync(firstBarber, customer.Id);
        await data.CreateReviewAsync(secondBarber, customer.Id);

        var admin = await app.CreateUserAsync(
            $"admin-summary-publish-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var before = await GetSummaryAsync(app);
        var previousOldest = before.Reviews.Oldest;

        Assert.NotNull(previousOldest);

        using (var publish = await app.Client.PostAsync(
                   $"api/reviews/{previousOldest.Id}/publication",
                   null))
        {
            Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        }

        var after = await GetSummaryAsync(app);
        var queueIds = await GetQueueIdsAsync(app);

        Assert.Equal(before.Reviews.PendingCount - 1, after.Reviews.PendingCount);
        Assert.NotNull(after.Reviews.Oldest);
        Assert.NotEqual(previousOldest.Id, after.Reviews.Oldest.Id);
        Assert.Equal(queueIds[0], after.Reviews.Oldest.Id);
    }

    [Fact(Timeout = 180_000)]
    public async Task Summary_does_not_expose_the_customer_email()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Summary Pii Barber");
        var customerEmail = $"admin-summary-pii-{Guid.NewGuid():N}@barbersearch.test";
        var customer = await app.CreateUserAsync(customerEmail);

        await data.CreateReviewAsync(barber, customer.Id);

        var admin = await app.CreateUserAsync(
            $"admin-summary-pii-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var summaryResponse = await app.Client.GetAsync(ENDPOINT);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);

        var rawBody = await summaryResponse.Content.ReadAsStringAsync();
        var summary = JsonSerializer.Deserialize<Summary>(rawBody, JsonOptions);
        Assert.NotNull(summary);
        Assert.NotNull(summary.Reviews.Oldest);

        // Эталон email берётся из карточки того же отзыва: контракт сводки его не отдаёт.
        using var detail = await app.Client.GetAsync($"{REVIEWS_ENDPOINT}/{summary.Reviews.Oldest.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var detailBody = await detail.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(detailBody);
        var email = document.RootElement
            .GetProperty("customer")
            .GetProperty("email")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(email));
        Assert.Equal(customerEmail, email, ignoreCase: true);
        Assert.DoesNotContain(email!, rawBody, StringComparison.OrdinalIgnoreCase);

        // Имена есть, служебных полей пагинации нет.
        Assert.Contains(summary.Reviews.Oldest.CustomerDisplayName, rawBody, StringComparison.Ordinal);
        Assert.Contains(summary.Reviews.Oldest.BarberDisplayName, rawBody, StringComparison.Ordinal);

        using var summaryDocument = JsonDocument.Parse(rawBody);
        var root = summaryDocument.RootElement;

        Assert.True(root.TryGetProperty("reviews", out _));
        Assert.True(root.TryGetProperty("barbers", out _));
        Assert.True(root.TryGetProperty("salons", out _));
        Assert.False(root.TryGetProperty("total", out _));
        Assert.False(root.TryGetProperty("nextCursor", out _));
    }

    private static async Task<Summary> GetSummaryAsync(TestApp app)
    {
        using var response = await app.Client.GetAsync(ENDPOINT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadSummaryAsync(response);
    }

    private static async Task<Summary> ReadSummaryAsync(HttpResponseMessage response)
    {
        var summary = JsonSerializer.Deserialize<Summary>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.NotNull(summary);

        return summary;
    }

    /// <summary>
    /// Идентификаторы очереди модерации в её собственном порядке: сводка обязана ссылаться
    /// на первую строку, а не на «самую старую по мнению сводки».
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> GetQueueIdsAsync(TestApp app)
    {
        using var response = await app.Client.GetAsync(
            $"{REVIEWS_ENDPOINT}?status=pending&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = JsonSerializer.Deserialize<ReviewPage>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.NotNull(page);

        return page.Items.Select(item => item.Id).ToList();
    }

    private sealed record Summary(
        SummaryReviews Reviews,
        SummaryBarbers Barbers,
        SummarySalons Salons);

    private sealed record SummaryReviews(int PendingCount, SummaryOldest? Oldest);

    private sealed record SummaryOldest(
        Guid Id,
        int Rating,
        DateTimeOffset CreatedAt,
        string BarberDisplayName,
        string CustomerDisplayName);

    private sealed record SummaryBarbers(int UnverifiedCount);

    private sealed record SummarySalons(int InactiveCount);

    private sealed record ReviewPage(IReadOnlyList<ReviewRow> Items, string? NextCursor);

    private sealed record ReviewRow(Guid Id);
}
