using System.Net;
using System.Text.Json;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
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
/// а не абсолютными числами: у сводки нет параметров, которые сузили бы выборку. Числа
/// счётчиков дополнительно сверяются с размерами очередей list-эндпоинтов — это и есть
/// контракт «счётчик = фильтр раздела». Сид меньше страницы (20 по умолчанию, 100 максимум),
/// поэтому пагинация не мешает.
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
        Assert.NotNull(summary.Photos);
        Assert.NotNull(summary.Users);
        Assert.NotNull(summary.BarberRequests);
        Assert.NotNull(summary.Bookings);

        // Чистая база: ни аватаров, ни работ на проверке.
        Assert.Equal(0, summary.Photos.PendingAvatarsCount);
        Assert.Equal(0, summary.Photos.PendingPortfolioCount);
        Assert.Equal(0, summary.Salons.PendingModerationCount);
        Assert.Equal(0, summary.BarberRequests.PendingCount);
        Assert.Equal(0, summary.Bookings.ConfirmedCount);

        // В базе ровно три засеянных пользователя (клиент, барбер, администратор),
        // и ни у одного email не подтверждён.
        Assert.Equal(3, summary.Users.UnverifiedCount);
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

        // Очередь фотографий: один аватар и одна работа, ждущие проверки. Файлы не нужны:
        // счётчик и очередь смотрят на строку и статус, а не на диск.
        var avatarUser = await app.CreateUserAsync(
            $"admin-summary-avatar-{Guid.NewGuid():N}@barbersearch.test");

        await SeedPendingAvatarAsync(app, avatarUser.Id);
        await SeedPendingPortfolioPhotoAsync(app, publishedBarber.Profile.Id);

        var after = await GetSummaryAsync(app);

        Assert.Equal(before.Reviews.PendingCount + 1, after.Reviews.PendingCount);
        Assert.Equal(before.Barbers.UnverifiedCount + 1, after.Barbers.UnverifiedCount);
        Assert.Equal(before.Salons.InactiveCount + 1, after.Salons.InactiveCount);
        Assert.Equal(before.Photos.PendingAvatarsCount + 1, after.Photos.PendingAvatarsCount);
        Assert.Equal(
            before.Photos.PendingPortfolioCount + 1,
            after.Photos.PendingPortfolioCount);

        // Числа сводки — ровно размеры вкладок очереди, а не отдельный подсчёт.
        Assert.Equal(
            after.Photos.PendingAvatarsCount,
            await GetPagedCountAsync(app, "api/admin/photos/avatars?pageSize=100"));
        Assert.Equal(
            after.Photos.PendingPortfolioCount,
            await GetPagedCountAsync(app, "api/admin/photos/portfolio?pageSize=100"));

        // Опубликованный отзыв счётчик не двигает: вкладка «На модерации» его не показывает.
        var queued = await GetQueueIdsAsync(app);
        Assert.All(queued, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(after.Reviews.PendingCount, queued.Count);
    }

    [Fact(Timeout = 180_000)]
    public async Task Section_counters_match_the_admin_queue_sizes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        // Барбер и администратор созданы до снятия «до»: их аккаунты тоже без подтверждения
        // email, и дельта пользователей не должна их считать.
        var barber = await data.CreateBookableBarberAsync("Summary Counters Barber");
        var admin = await app.CreateUserAsync(
            $"admin-summary-counters-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        var before = await GetSummaryAsync(app);

        // Два новых пользователя без подтверждения email. Они же заявители и владельцы
        // салонов, поэтому других пользователей в сценарии не появляется.
        var firstApplicant = await app.CreateUserAsync(
            $"admin-summary-counters-first-{Guid.NewGuid():N}@barbersearch.test");
        var secondApplicant = await app.CreateUserAsync(
            $"admin-summary-counters-second-{Guid.NewGuid():N}@barbersearch.test");

        // Очередь заявок — две Pending-заявки от разных пользователей.
        await SeedPendingBarberRequestAsync(app, firstApplicant.Id);
        await SeedPendingBarberRequestAsync(app, secondApplicant.Id);

        // Очередь модерации салонов — два Pending-салона.
        await SeedPendingSalonAsync(app, firstApplicant.Id, "Summary Moderation Salon One");
        await SeedPendingSalonAsync(app, secondApplicant.Id, "Summary Moderation Salon Two");

        // Подтверждённые брони: три в разные слоты, иначе exclusion-ограничение барбера
        // не пропустит две брони подряд. Плюс одна Pending — она в счётчик не входит.
        var startAt = DateTimeOffset.UtcNow.AddDays(3);
        await data.CreateBookingAsync(
            barber,
            firstApplicant.Id,
            startAt,
            BookingStatus.Confirmed);
        await data.CreateBookingAsync(
            barber,
            secondApplicant.Id,
            startAt.AddHours(1),
            BookingStatus.Confirmed);
        await data.CreateBookingAsync(
            barber,
            firstApplicant.Id,
            startAt.AddHours(2),
            BookingStatus.Confirmed);
        await data.CreateBookingAsync(
            barber,
            secondApplicant.Id,
            startAt.AddHours(3),
            BookingStatus.Pending);

        var after = await GetSummaryAsync(app);

        Assert.Equal(before.Users.UnverifiedCount + 2, after.Users.UnverifiedCount);
        Assert.Equal(before.BarberRequests.PendingCount + 2, after.BarberRequests.PendingCount);
        Assert.Equal(
            before.Salons.PendingModerationCount + 2,
            after.Salons.PendingModerationCount);
        Assert.Equal(before.Bookings.ConfirmedCount + 3, after.Bookings.ConfirmedCount);

        // Pending-салон неактивен, но он не «выключен администратором»: другой счётчик
        // салонов остаётся прежним.
        Assert.Equal(before.Salons.InactiveCount, after.Salons.InactiveCount);

        // Числа сводки — ровно размеры очередей соответствующих list-эндпоинтов.
        // «Пользователи» — все роли без подтверждения email (role=all по умолчанию).
        Assert.Equal(
            after.Users.UnverifiedCount,
            await GetPagedCountAsync(
                app,
                "api/admin/users?status=unverified&role=all&pageSize=100"));
        Assert.Equal(
            after.BarberRequests.PendingCount,
            await GetPagedCountAsync(
                app,
                "api/admin/barber-requests?status=pending&pageSize=100"));
        Assert.Equal(
            after.Salons.PendingModerationCount,
            await GetArrayCountAsync(app, "api/admin/catalogue/salons?status=pending"));
        Assert.Equal(
            after.Bookings.ConfirmedCount,
            await GetPagedCountAsync(
                app,
                "api/admin/bookings?status=Confirmed&pageSize=100"));
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

        // JSON аддитивный: старые поля на месте, новые — в конце, PII ни в одном нет.
        Assert.True(root.TryGetProperty("reviews", out _));
        Assert.True(root.TryGetProperty("barbers", out _));
        Assert.True(root.TryGetProperty("salons", out _));
        Assert.True(root.TryGetProperty("photos", out _));
        Assert.True(root.TryGetProperty("users", out _));
        Assert.True(root.TryGetProperty("barberRequests", out _));
        Assert.True(root.TryGetProperty("bookings", out _));
        Assert.True(root.GetProperty("salons").TryGetProperty("pendingModerationCount", out _));
        Assert.False(root.TryGetProperty("total", out _));
        Assert.False(root.TryGetProperty("nextCursor", out _));
    }

    /// <summary>
    /// Ставит пользователю новый аватар доменным методом: он уходит в <c>Pending</c>,
    /// получает техническую метку поступления и попадает в счётчик сводки. Файл не нужен.
    /// </summary>
    private static async Task SeedPendingAvatarAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);

        var avatarUrl = UserAvatarMediaUrl.Build(userId, Guid.CreateVersion7(), "png");

        Assert.False(user.SetAvatar(avatarUrl).IsFailure);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Добавляет профилю загруженную (Pending) фотографию портфолио без файла: очередь
    /// и счётчик читают строку, а раздача в этом тесте не участвует.
    /// </summary>
    private static async Task SeedPendingPortfolioPhotoAsync(TestApp app, Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(candidate => candidate.PortfolioPhotos)
            .SingleAsync(candidate => candidate.Id == barberProfileId);

        var photoUrl = PortfolioMediaUrl.Build(
            barberProfileId,
            Guid.CreateVersion7(),
            "png");

        Assert.False(profile.AddUploadedPhoto(photoUrl, "Summary pending photo").IsFailure);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Добавляет заявку в статусе <c>Pending</c> — строку очереди «Заявки».
    /// </summary>
    private static async Task<Guid> SeedPendingBarberRequestAsync(TestApp app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var request = Require(BarberRequest.Create(
            userId,
            "Summary Applicant",
            5,
            "Berlin",
            null,
            hasSalon: false,
            DateTimeOffset.UtcNow));

        db.BarberRequests.Add(request);
        await db.SaveChangesAsync();

        return request.Id;
    }

    /// <summary>
    /// Добавляет салон на модерации доменной фабрикой владельца: он сразу
    /// <c>Pending</c> и неактивен — ровно строка очереди «На проверке».
    /// </summary>
    private static async Task<Guid> SeedPendingSalonAsync(
        TestApp app,
        Guid ownerUserId,
        string name)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var salon = Require(Salon.SubmitByOwner(
            ownerUserId,
            name,
            "Teststrasse 2",
            "Berlin",
            "Germany",
            "Europe/Berlin",
            52.5200,
            13.4050));

        db.Salons.Add(salon);
        await db.SaveChangesAsync();

        return salon.Id;
    }

    /// <summary>
    /// Размер paged-очереди: число строк первой страницы (до 100). Сид теста меньше
    /// страницы, поэтому пагинация не мешает.
    /// </summary>
    private static async Task<int> GetPagedCountAsync(TestApp app, string path)
    {
        using var response = await app.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("items").GetArrayLength();
    }

    /// <summary>
    /// Размер очереди, которая отдаётся голым JSON-массивом (салоны каталога).
    /// </summary>
    private static async Task<int> GetArrayCountAsync(TestApp app, string path)
    {
        using var response = await app.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetArrayLength();
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

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private sealed record Summary(
        SummaryReviews Reviews,
        SummaryBarbers Barbers,
        SummarySalons Salons,
        SummaryPhotos Photos,
        SummaryUsers Users,
        SummaryBarberRequests BarberRequests,
        SummaryBookings Bookings);

    private sealed record SummaryReviews(int PendingCount, SummaryOldest? Oldest);

    private sealed record SummaryOldest(
        Guid Id,
        int Rating,
        DateTimeOffset AwaitingSince,
        string BarberDisplayName,
        string CustomerDisplayName);

    private sealed record SummaryBarbers(int UnverifiedCount);

    private sealed record SummarySalons(int InactiveCount, int PendingModerationCount);

    private sealed record SummaryPhotos(int PendingAvatarsCount, int PendingPortfolioCount);

    private sealed record SummaryUsers(int UnverifiedCount);

    private sealed record SummaryBarberRequests(int PendingCount);

    private sealed record SummaryBookings(int ConfirmedCount);

    private sealed record ReviewPage(IReadOnlyList<ReviewRow> Items, string? NextCursor);

    private sealed record ReviewRow(Guid Id);
}
