using BarberSearch.Application.Reviews.GetReviews;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Отзывы: только опубликованные, фильтр по барберу, пагинация и битый курсор.
/// </summary>
public class ReviewReadTests
{
    [Fact]
    public async Task Get_ReturnsOnlyPublishedReviewsOfRequestedBarber()
    {
        var barberId = Guid.CreateVersion7();
        var otherBarberId = Guid.CreateVersion7();
        var repository = new FakeReviewRepository();
        repository.Add(CreateReview(barberId, "Published", isPublished: true));
        repository.Add(CreateReview(barberId, "Hidden", isPublished: false));
        repository.Add(CreateReview(otherBarberId, "Other barber", isPublished: true));
        var handler = new GetReviewsHandler(repository);

        var result = await handler.Handle(
            new GetReviewsQuery(barberId, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("Published", result.Value.Items[0].Text);
        Assert.True(result.Value.Items[0].IsPublished);
    }

    [Fact]
    public async Task Get_FiltersByBarberProfileId()
    {
        var barberId = Guid.CreateVersion7();
        var otherBarberId = Guid.CreateVersion7();
        var repository = new FakeReviewRepository();
        repository.Add(CreateReview(barberId, "First", isPublished: true));
        repository.Add(CreateReview(otherBarberId, "Second", isPublished: true));
        var handler = new GetReviewsHandler(repository);

        var result = await handler.Handle(
            new GetReviewsQuery(otherBarberId, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("Second", result.Value.Items[0].Text);
    }

    [Fact]
    public async Task Get_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var barberId = Guid.CreateVersion7();
        var repository = new FakeReviewRepository();
        var expected = new List<Review>();

        for (var index = 0; index < 5; index++)
        {
            Thread.Sleep(5);
            var review = CreateReview(barberId, $"Review {index}", isPublished: true);
            expected.Add(review);
            repository.Add(review);
        }

        var handler = new GetReviewsHandler(repository);
        var collected = new List<ReviewResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new GetReviewsQuery(barberId, cursor, 2),
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
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var handler = new GetReviewsHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetReviewsQuery(Guid.CreateVersion7(), "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_WithEmptyBarberProfileId_ReturnsValidation()
    {
        var handler = new GetReviewsHandler(new FakeReviewRepository());

        var result = await handler.Handle(
            new GetReviewsQuery(Guid.Empty, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetReviewsHandler(new FakeReviewRepository());

        var tooSmall = await handler.Handle(
            new GetReviewsQuery(Guid.CreateVersion7(), null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetReviewsQuery(Guid.CreateVersion7(), null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge.Error.Errors[0].Type);
    }

    private static Review CreateReview(Guid barberProfileId, string text, bool isPublished)
    {
        var startAt = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)
            .AddMinutes(Random.Shared.Next(0, 1000));
        var booking = Booking.Create(
            Guid.CreateVersion7(),
            barberProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            30m,
            $"key-{Guid.NewGuid()}").Value;
        var review = Review.Create(booking, booking.UserId, 5, text).Value;

        if (!isPublished)
        {
            review.Hide();
        }

        return review;
    }
}
