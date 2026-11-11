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
/// последовательные чтения и проброс ошибок каждого из четырёх репозиториев.
/// </summary>
public class AdminSummaryHandlerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

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
        var salons = new FakeSalonRepository { InactiveCount = 1 };
        var users = new FakeUserRepository { PendingAvatarsCount = 4 };

        var result = await HandleAsync(reviews, barbers, salons, users);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Reviews.PendingCount);
        Assert.Equal(2, result.Value.Barbers.UnverifiedCount);
        Assert.Equal(1, result.Value.Salons.InactiveCount);
        Assert.Equal(4, result.Value.Photos.PendingAvatarsCount);
        Assert.Equal(7, result.Value.Photos.PendingPortfolioCount);

        var oldestSummary = Assert.IsType<AdminSummaryOldestReviewResponse>(result.Value.Reviews.Oldest);
        Assert.Equal(oldest.Id, oldestSummary.Id);
        Assert.Equal(oldest.Rating, oldestSummary.Rating);
        Assert.Equal(oldest.UpdatedAt, oldestSummary.AwaitingSince);
        Assert.Equal(oldest.Barber.DisplayName, oldestSummary.BarberDisplayName);
        Assert.Equal(oldest.Customer.FullName, oldestSummary.CustomerDisplayName);
    }

    [Fact]
    public async Task Handle_ReadsPhotoCountersSequentially()
    {
        var barbers = new FakeBarberProfileRepository { PendingPortfolioCount = 3 };
        var users = new FakeUserRepository { PendingAvatarsCount = 2 };
        var order = new List<string>();

        users.BeforeCountPendingAvatars = () => order.Add("avatars");
        barbers.BeforeCountPendingPortfolioPhotos = () =>
        {
            // К этому моменту счётчик аватаров уже прочитан: WhenAll стартовал бы оба
            // чтения до первого результата, а один DbContext этого не допускает.
            Assert.Equal(1, users.CountPendingAvatarsCallCount);
            order.Add("portfolio");
        };

        var result = await HandleAsync(
            new FakeReviewRepository(),
            barbers,
            users: users);

        Assert.True(result.IsSuccess);
        Assert.Equal("avatars|portfolio", string.Join('|', order));
        Assert.Equal(1, users.CountPendingAvatarsCallCount);
        Assert.Equal(1, barbers.CountPendingPortfolioCallCount);
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

    private static Task<CSharpFunctionalExtensions.Result<AdminSummaryResponse, Failure>> HandleAsync(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository? barbers = null,
        FakeSalonRepository? salons = null,
        FakeUserRepository? users = null)
    {
        var handler = new GetAdminSummaryHandler(
            reviews,
            barbers ?? new FakeBarberProfileRepository(),
            salons ?? new FakeSalonRepository(),
            users ?? new FakeUserRepository());

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
