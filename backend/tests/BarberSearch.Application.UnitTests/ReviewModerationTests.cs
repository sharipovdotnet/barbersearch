using BarberSearch.Application.Reviews.GetModerationQueue;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Очередь модерации: только скрытые отзывы всех барберов, старые сверху, keyset-пагинация
/// и отклонение недопустимого размера страницы (R-35).
/// </summary>
public class ReviewModerationTests
{
    [Fact]
    public async Task Get_ReturnsHiddenReviewsOldestFirstAcrossPages()
    {
        var repository = new FakeReviewRepository();

        // Между созданиями — пауза: CreatedAt задаётся системными часами, и порядок должен
        // быть однозначным, а не зависеть от разрешения таймера.
        var hiddenOldest = CreateReview(Guid.CreateVersion7(), "Hidden oldest", published: false);
        Thread.Sleep(5);
        var published = CreateReview(Guid.CreateVersion7(), "Published", published: true);
        Thread.Sleep(5);
        var hiddenNewest = CreateReview(Guid.CreateVersion7(), "Hidden newest", published: false);
        repository.Add(hiddenOldest);
        repository.Add(published);
        repository.Add(hiddenNewest);

        var handler = new GetModerationQueueHandler(repository);

        var firstPage = await handler.Handle(
            new GetModerationQueueQuery(null, 1),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        var first = Assert.Single(firstPage.Value.Items);
        Assert.Equal(hiddenOldest.Id, first.Id);
        Assert.False(first.IsPublished);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await handler.Handle(
            new GetModerationQueueQuery(firstPage.Value.NextCursor, 1),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        var second = Assert.Single(secondPage.Value.Items);
        Assert.Equal(hiddenNewest.Id, second.Id);
        Assert.False(second.IsPublished);
        Assert.Null(secondPage.Value.NextCursor);

        Assert.DoesNotContain(published.Id, new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetModerationQueueHandler(new FakeReviewRepository());

        var tooSmall = await handler.Handle(
            new GetModerationQueueQuery(null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetModerationQueueQuery(null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge.Error.Errors[0].Type);
        Assert.Equal("review.moderation.pageSize", tooSmall.Error.Errors[0].Code);
        Assert.Equal("review.moderation.pageSize", tooLarge.Error.Errors[0].Code);
    }

    private static Review CreateReview(Guid barberProfileId, string text, bool published)
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

        // R-35: Create оставляет отзыв скрытым; опубликованный для контраста включаем явно.
        if (published)
        {
            review.Publish();
        }

        return review;
    }
}
