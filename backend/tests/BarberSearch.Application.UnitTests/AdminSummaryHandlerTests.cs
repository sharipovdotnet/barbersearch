using System.Text.Json;
using BarberSearch.Application.Admin.GetAdminSummary;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Reviews.Admin;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Обработчик админской сводки: маппинг счётчиков и «самого старого» отзыва, переиспользование
/// FIFO-страницы, <c>null</c> при пустой или успевшей опустеть очереди, две очереди фотографий,
/// счётчики пользователей, заявок и броней, последовательные чтения и проброс ошибок каждого
/// из шести репозиториев.
/// </summary>
public class AdminSummaryHandlerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] SummaryFieldOrder =
    [
        "reviews",
        "barbers",
        "salons",
        "photos",
        "users",
        "barberRequests",
        "bookings"
    ];

    private static readonly string[] SalonFieldOrder =
    [
        "inactiveCount",
        "pendingModerationCount"
    ];

    [Fact]
    public async Task Handle_MapsCountsAndCompactOldest()
    {
        var reviews = new FakeReviewRepository { PendingCount = 3 };
        var oldest = CreateReview(Start, "Самый старый");
        reviews.AddAdminReview(oldest);
        reviews.AddAdminReview(CreateReview(Start.AddHours(1), "Свежее"));
        var barbers = new FakeBarberProfileRepository
        {
            PendingCount = 2,
            PendingPortfolioCount = 7
        };
        var salons = new FakeSalonRepository
        {
            InactiveCount = 1,
            PendingModerationCount = 5
        };
        var users = new FakeUserRepository
        {
            PendingAvatarsCount = 4,
            UnverifiedCount = 6
        };
        var barberRequests = new FakeBarberRequestRepository { PendingCount = 8 };
        var bookings = new FakeBookingRepository { ConfirmedCount = 9 };

        var result = await HandleAsync(
            reviews,
            barbers,
            salons,
            users,
            barberRequests,
            bookings);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Reviews.PendingCount);
        Assert.Equal(2, result.Value.Barbers.UnverifiedCount);
        Assert.Equal(1, result.Value.Salons.InactiveCount);
        Assert.Equal(5, result.Value.Salons.PendingModerationCount);
        Assert.Equal(4, result.Value.Photos.PendingAvatarsCount);
        Assert.Equal(7, result.Value.Photos.PendingPortfolioCount);
        Assert.Equal(6, result.Value.Users.UnverifiedCount);
        Assert.Equal(8, result.Value.BarberRequests.PendingCount);
        Assert.Equal(9, result.Value.Bookings.ConfirmedCount);

        var oldestSummary = Assert.IsType<AdminSummaryOldestReviewResponse>(result.Value.Reviews.Oldest);
        Assert.Equal(oldest.Id, oldestSummary.Id);
        Assert.Equal(oldest.Rating, oldestSummary.Rating);
        Assert.Equal(oldest.UpdatedAt, oldestSummary.AwaitingSince);
        Assert.Equal(oldest.Barber.DisplayName, oldestSummary.BarberDisplayName);
        Assert.Equal(oldest.Customer.FullName, oldestSummary.CustomerDisplayName);
    }

    [Fact]
    public async Task Handle_WithEmptyDatabase_ReturnsZeros()
    {
        var result = await HandleAsync(new FakeReviewRepository());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Reviews.PendingCount);
        Assert.Equal(0, result.Value.Barbers.UnverifiedCount);
        Assert.Equal(0, result.Value.Salons.InactiveCount);
        Assert.Equal(0, result.Value.Salons.PendingModerationCount);
        Assert.Equal(0, result.Value.Photos.PendingAvatarsCount);
        Assert.Equal(0, result.Value.Photos.PendingPortfolioCount);
        Assert.Equal(0, result.Value.Users.UnverifiedCount);
        Assert.Equal(0, result.Value.BarberRequests.PendingCount);
        Assert.Equal(0, result.Value.Bookings.ConfirmedCount);
        Assert.Null(result.Value.Reviews.Oldest);
    }

    [Fact]
    public async Task Handle_ReadsSectionCountersSequentially()
    {
        var barbers = new FakeBarberProfileRepository { PendingPortfolioCount = 3 };
        var users = new FakeUserRepository { PendingAvatarsCount = 2, UnverifiedCount = 6 };
        var barberRequests = new FakeBarberRequestRepository { PendingCount = 8 };
        var bookings = new FakeBookingRepository { ConfirmedCount = 9 };
        var order = new List<string>();

        users.BeforeCountPendingAvatars = () => order.Add("avatars");
        barbers.BeforeCountPendingPortfolioPhotos = () =>
        {
            // К этому моменту счётчик аватаров уже прочитан: WhenAll стартовал бы оба
            // чтения до первого результата, а один DbContext этого не допускает.
            Assert.Equal(1, users.CountPendingAvatarsCallCount);
            order.Add("portfolio");
        };
        users.BeforeCountUnverified = () =>
        {
            Assert.Equal(1, barbers.CountPendingPortfolioCallCount);
            order.Add("users");
        };
        barberRequests.BeforeCountPending = () =>
        {
            Assert.Equal(1, users.CountUnverifiedCallCount);
            order.Add("barberRequests");
        };
        bookings.BeforeCountConfirmed = () =>
        {
            Assert.Equal(1, barberRequests.CountPendingCallCount);
            order.Add("bookings");
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            barbers,
            users: users,
            barberRequests: barberRequests,
            bookings: bookings);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "avatars|portfolio|users|barberRequests|bookings",
            string.Join('|', order));
        Assert.Equal(1, users.CountPendingAvatarsCallCount);
        Assert.Equal(1, barbers.CountPendingPortfolioCallCount);
        Assert.Equal(1, users.CountUnverifiedCallCount);
        Assert.Equal(1, barberRequests.CountPendingCallCount);
        Assert.Equal(1, bookings.CountConfirmedCallCount);
    }

    [Fact]
    public async Task Handle_ReadsTheFirstRowOfTheModerationQueue()
    {
        var reviews = new FakeReviewRepository { PendingCount = 1 };
        reviews.AddAdminReview(CreateReview(Start, "Единственный"));

        var result = await HandleAsync(reviews);

        Assert.True(result.IsSuccess);
        Assert.Null(reviews.LastModerationCursor);
        Assert.Equal(1, reviews.LastModerationLimit);
    }

    [Fact]
    public async Task Handle_WithEmptyQueue_ReturnsNullOldest()
    {
        var reviews = new FakeReviewRepository { PendingCount = 0 };

        var result = await HandleAsync(reviews);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Reviews.PendingCount);
        Assert.Null(result.Value.Reviews.Oldest);
    }

    [Fact]
    public async Task Handle_WhenTheQueueEmptiedBetweenTwoReads_KeepsTheCount()
    {
        // Гонка двух чтений: отзыв опубликовали между COUNT и страницей. Это не ошибка —
        // экран покажет число, а «самого старого» просто нет.
        var reviews = new FakeReviewRepository { PendingCount = 2 };

        var result = await HandleAsync(reviews);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Reviews.PendingCount);
        Assert.Null(result.Value.Reviews.Oldest);
    }

    [Fact]
    public async Task Handle_PropagatesReviewCountFailure()
    {
        var reviews = new FakeReviewRepository
        {
            CountPendingFailure = ValidationFailure("summary.reviews.count")
        };

        var result = await HandleAsync(reviews);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.reviews.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesModerationQueueFailure()
    {
        var reviews = new FakeReviewRepository
        {
            AdminPageFailure = ValidationFailure("summary.reviews.page")
        };

        var result = await HandleAsync(reviews);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.reviews.page", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesBarberCountFailure()
    {
        var barbers = new FakeBarberProfileRepository
        {
            CountPendingFailure = ValidationFailure("summary.barbers.count")
        };

        var result = await HandleAsync(new FakeReviewRepository(), barbers);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.barbers.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesSalonCountFailure()
    {
        var salons = new FakeSalonRepository
        {
            CountInactiveFailure = ValidationFailure("summary.salons.count")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            new FakeBarberProfileRepository(),
            salons);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.salons.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesPendingModerationCountFailure()
    {
        var salons = new FakeSalonRepository
        {
            CountPendingModerationFailure = ValidationFailure("summary.salons.pendingModeration")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            new FakeBarberProfileRepository(),
            salons);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "summary.salons.pendingModeration",
            result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesAvatarCountFailure()
    {
        var users = new FakeUserRepository
        {
            CountPendingAvatarsFailure = ValidationFailure("summary.avatars.count")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            new FakeBarberProfileRepository(),
            users: users);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.avatars.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesPortfolioCountFailure()
    {
        var barbers = new FakeBarberProfileRepository
        {
            CountPendingPortfolioFailure = ValidationFailure("summary.portfolio.count")
        };

        var result = await HandleAsync(new FakeReviewRepository(), barbers);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.portfolio.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesUnverifiedUserCountFailure()
    {
        var users = new FakeUserRepository
        {
            CountUnverifiedFailure = ValidationFailure("summary.users.count")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            users: users);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.users.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesBarberRequestCountFailure()
    {
        var barberRequests = new FakeBarberRequestRepository
        {
            CountPendingFailure = ValidationFailure("summary.barberRequests.count")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            barberRequests: barberRequests);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.barberRequests.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_PropagatesBookingCountFailure()
    {
        var bookings = new FakeBookingRepository
        {
            CountConfirmedFailure = ValidationFailure("summary.bookings.count")
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            bookings: bookings);

        Assert.True(result.IsFailure);
        Assert.Equal("summary.bookings.count", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Response_AppendsCountersWithoutMovingOldFields()
    {
        // JSON аддитивный: новые поля появляются в конце, старые не сдвигаются. Порядок
        // входит в контракт для фронтенда, поэтому проверяется сериализованный ответ,
        // а не только типы.
        var reviews = new FakeReviewRepository { PendingCount = 1 };
        reviews.AddAdminReview(CreateReview(Start, "Первый"));

        var result = await HandleAsync(
            reviews,
            users: new FakeUserRepository { UnverifiedCount = 4 },
            barberRequests: new FakeBarberRequestRepository { PendingCount = 2 },
            bookings: new FakeBookingRepository { ConfirmedCount = 3 });

        Assert.True(result.IsSuccess);

        var json = JsonSerializer.Serialize(result.Value, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(
            SummaryFieldOrder,
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(
            SalonFieldOrder,
            root.GetProperty("salons")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(2, root.GetProperty("barberRequests").GetProperty("pendingCount").GetInt32());
        Assert.Equal(3, root.GetProperty("bookings").GetProperty("confirmedCount").GetInt32());
        Assert.Equal(4, root.GetProperty("users").GetProperty("unverifiedCount").GetInt32());
    }

    [Fact]
    public void SectionCountCards_ExposeOnlyTheirCount()
    {
        Assert.Equal(
            nameof(AdminSummaryUsersResponse.UnverifiedCount),
            Assert.Single(PropertyNames(typeof(AdminSummaryUsersResponse))));
        Assert.Equal(
            nameof(AdminSummaryBarberRequestsResponse.PendingCount),
            Assert.Single(PropertyNames(typeof(AdminSummaryBarberRequestsResponse))));
        Assert.Equal(
            nameof(AdminSummaryBookingsResponse.ConfirmedCount),
            Assert.Single(PropertyNames(typeof(AdminSummaryBookingsResponse))));

        var salonProperties = PropertyNames(typeof(AdminSummarySalonsResponse));

        Assert.Equal(2, salonProperties.Length);
        Assert.Equal(nameof(AdminSummarySalonsResponse.InactiveCount), salonProperties[0]);
        Assert.Equal(
            nameof(AdminSummarySalonsResponse.PendingModerationCount),
            salonProperties[1]);
    }

    [Fact]
    public void OldestReviewContract_HasNoEmail()
    {
        // PII не добавляется: в сводке только имена. Контракт — часть обещания,
        // поэтому проверяется целиком, а не только тем, что экран «не читает» email.
        var properties = typeof(AdminSummaryOldestReviewResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(AdminSummaryOldestReviewResponse.Id),
                nameof(AdminSummaryOldestReviewResponse.Rating),
                nameof(AdminSummaryOldestReviewResponse.AwaitingSince),
                nameof(AdminSummaryOldestReviewResponse.BarberDisplayName),
                nameof(AdminSummaryOldestReviewResponse.CustomerDisplayName)
            },
            properties);
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Email", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Phone", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] PropertyNames(Type type)
        => type.GetProperties().Select(property => property.Name).ToArray();

    private static Task<CSharpFunctionalExtensions.Result<AdminSummaryResponse, Failure>> HandleAsync(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository? barbers = null,
        FakeSalonRepository? salons = null,
        FakeUserRepository? users = null,
        FakeBarberRequestRepository? barberRequests = null,
        FakeBookingRepository? bookings = null)
    {
        var handler = new GetAdminSummaryHandler(
            reviews,
            barbers ?? new FakeBarberProfileRepository(),
            salons ?? new FakeSalonRepository(),
            users ?? new FakeUserRepository(),
            barberRequests ?? new FakeBarberRequestRepository(),
            bookings ?? new FakeBookingRepository());

        return handler.Handle(new GetAdminSummaryQuery(), CancellationToken.None);
    }

    private static Failure ValidationFailure(string code)
        => new(Error.Validation(code, "The summary read failed.", "summary"));

    private static AdminReviewResponse CreateReview(DateTimeOffset createdAt, string text)
        => new(
            Guid.CreateVersion7(),
            4,
            text,
            false,
            createdAt,
            createdAt,
            new AdminReviewBarberResponse(
                Guid.CreateVersion7(),
                "Артём Соколов",
                4.8m,
                24,
                true,
                true),
            new AdminReviewCustomerResponse(
                Guid.CreateVersion7(),
                "Дмитрий Крылов",
                "dmitry@example.com"),
            new AdminReviewVisitResponse(
                Guid.CreateVersion7(),
                createdAt.AddDays(-2),
                2400m,
                "Fade",
                "Барбершоп на Тверской",
                "Europe/Moscow"));
}
