using BarberSearch.Application.Reviews.Admin.GetAdminReview;
using BarberSearch.Application.Reviews.Admin.GetAdminReviews;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Reviews.Admin;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Админское чтение отзывов: фильтр по статусу, направление страниц, курсор и карточка по id.
/// </summary>
public class AdminReviewReadTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_Pending_ReturnsOldestFirstAcrossPages()
    {
        var repository = new FakeReviewRepository();
        var oldest = CreateReview(published: false, Start, "Oldest");
        var middle = CreateReview(published: false, Start.AddMinutes(10), "Middle");
        var newest = CreateReview(published: false, Start.AddMinutes(20), "Newest");
        repository.AddAdminReview(oldest);
        repository.AddAdminReview(middle);
        repository.AddAdminReview(newest);
        repository.AddAdminReview(CreateReview(published: true, Start.AddMinutes(30), "Published"));

        var handler = new GetAdminReviewsHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminReviewsQuery("pending", null, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(
            new[] { oldest.Id, middle.Id },
            firstPage.Value.Items.Select(review => review.Id));
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await handler.Handle(
            new GetAdminReviewsQuery("pending", firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(new[] { newest.Id }, secondPage.Value.Items.Select(review => review.Id));
        Assert.Null(secondPage.Value.NextCursor);
    }

    [Fact]
    public async Task Get_Published_ReturnsNewestFirstAndHidesPending()
    {
        var repository = new FakeReviewRepository();
        var older = CreateReview(published: true, Start, "Older");
        var newer = CreateReview(published: true, Start.AddHours(1), "Newer");
        repository.AddAdminReview(older);
        repository.AddAdminReview(newer);
        repository.AddAdminReview(CreateReview(published: false, Start.AddHours(2), "Pending"));

        var handler = new GetAdminReviewsHandler(repository);

        var result = await handler.Handle(
            new GetAdminReviewsQuery("published", null, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { newer.Id, older.Id },
            result.Value.Items.Select(review => review.Id));
        Assert.All(result.Value.Items, review => Assert.True(review.IsPublished));
    }

    [Fact]
    public async Task Get_WithUnknownStatus_ReturnsValidation()
    {
        var handler = new GetAdminReviewsHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetAdminReviewsQuery("rejected", null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.reviews.status", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetAdminReviewsHandler(new FakeReviewRepository());

        var tooSmall = await handler.Handle(
            new GetAdminReviewsQuery(null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetAdminReviewsQuery(null, null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal("admin.reviews.pageSize", tooSmall.Error.Errors[0].Code);
        Assert.Equal("admin.reviews.pageSize", tooLarge.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var handler = new GetAdminReviewsHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetAdminReviewsQuery(null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task GetById_ReturnsReviewWithContext()
    {
        var repository = new FakeReviewRepository();
        var review = CreateReview(published: false, Start, "Hidden");
        repository.AddAdminReview(review);

        var handler = new GetAdminReviewHandler(repository);

        var result = await handler.Handle(
            new GetAdminReviewQuery(review.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(review.Id, result.Value.Id);
        Assert.Equal("Артём Соколов", result.Value.Barber.DisplayName);
        Assert.Equal("Дмитрий Крылов", result.Value.Customer.FullName);
        Assert.Equal("dmitry@example.com", result.Value.Customer.Email);
        Assert.Equal("Fade", result.Value.Visit.ServiceName);
    }

    [Fact]
    public async Task GetById_WhenMissing_ReturnsNotFound()
    {
        var handler = new GetAdminReviewHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetAdminReviewQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("review.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_WithEmptyId_ReturnsValidation()
    {
        var handler = new GetAdminReviewHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetAdminReviewQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    private static AdminReviewResponse CreateReview(
        bool published,
        DateTimeOffset createdAt,
        string text)
        => new(
            Guid.CreateVersion7(),
            5,
            text,
            published,
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
