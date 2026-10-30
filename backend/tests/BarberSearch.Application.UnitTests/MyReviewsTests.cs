using BarberSearch.Application.Reviews.GetMyReviews;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// «Мои отзывы»: только свои, оба статуса, порядок, keyset-пагинация и ошибки параметров.
/// </summary>
public class MyReviewsTests
{
    [Fact]
    public async Task Get_ReturnsOnlyOwnReviewsInBothStatuses()
    {
        var authorId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();
        var repository = new FakeReviewRepository();

        var published = CreateReview(authorId, "Published", isPublished: true);
        repository.Add(published);

        Thread.Sleep(5);

        var hidden = CreateReview(authorId, "Hidden", isPublished: false);
        repository.Add(hidden);

        // Чужой отзыв не должен попасть в кабинет: фильтр по автору, а не по барберу.
        repository.Add(CreateReview(otherUserId, "Not mine", isPublished: true));

        var handler = new GetMyReviewsHandler(repository, new FakeCurrentUser(authorId));

        var result = await handler.Handle(new GetMyReviewsQuery(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(new[] { hidden.Id, published.Id }, result.Value.Items.Select(item => item.Id));

        // Статус — единственное, что различает бэкенд: скрытый отзыв тоже в списке.
        Assert.True(result.Value.Items[1].IsPublished);
        Assert.False(result.Value.Items[0].IsPublished);
        Assert.Null(result.Value.NextCursor);
    }

    [Fact]
    public async Task Get_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var authorId = Guid.CreateVersion7();
        var repository = new FakeReviewRepository();
        var expected = new List<Review>();

        for (var index = 0; index < 5; index++)
        {
            Thread.Sleep(5);
            var review = CreateReview(authorId, $"Review {index}", isPublished: index % 2 == 0);
            expected.Add(review);
            repository.Add(review);
        }

        var handler = new GetMyReviewsHandler(repository, new FakeCurrentUser(authorId));
        var collected = new List<MyReviewResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new GetMyReviewsQuery(cursor, 2),
                CancellationToken.None);

            Assert.True(page.IsSuccess);
            Assert.True(page.Value.Items.Count <= 2);
            collected.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        var expectedOrder = expected
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Select(review => review.Id);

        Assert.Equal(expectedOrder, collected.Select(item => item.Id));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Get_WithoutReviews_ReturnsEmptyPage()
    {
        var handler = new GetMyReviewsHandler(
            new FakeReviewRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(new GetMyReviewsQuery(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Null(result.Value.NextCursor);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidationWithOwnCode()
    {
        var handler = new GetMyReviewsHandler(
            new FakeReviewRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()));

        var tooSmall = await handler.Handle(new GetMyReviewsQuery(null, 0), CancellationToken.None);
        var tooLarge = await handler.Handle(new GetMyReviewsQuery(null, 101), CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Review.GetMyReviewsPageSizeInvalid,
            tooSmall.Error.Errors[0].Code);
        Assert.Equal(
            ApiErrorCodes.Review.GetMyReviewsPageSizeInvalid,
            tooLarge.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsInvalidCursor()
    {
        var handler = new GetMyReviewsHandler(
            new FakeReviewRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetMyReviewsQuery("broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("review.cursor.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_ForAnonymous_ReturnsUnauthorized()
    {
        var handler = new GetMyReviewsHandler(
            new FakeReviewRepository(),
            FakeCurrentUser.Anonymous());

        var result = await handler.Handle(new GetMyReviewsQuery(null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.Unauthorized, result.Error.Errors[0].Code);
    }

    private static Review CreateReview(Guid userId, string text, bool isPublished)
    {
        var startAt = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)
            .AddMinutes(Random.Shared.Next(0, 1000));
        var booking = Booking.Create(
            userId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            30m,
            $"key-{Guid.NewGuid()}").Value;
        var review = Review.Create(booking, userId, 5, text).Value;

        // R-35: Create оставляет отзыв скрытым; для опубликованных тестов публикуем явно.
        if (isPublished)
        {
            review.Publish();
        }

        return review;
    }
}
