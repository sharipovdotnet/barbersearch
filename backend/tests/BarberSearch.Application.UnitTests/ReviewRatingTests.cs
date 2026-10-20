using BarberSearch.Application.Reviews;
using BarberSearch.Application.Reviews.CreateReview;
using BarberSearch.Application.Reviews.HideReview;
using BarberSearch.Application.Reviews.PublishReview;
using BarberSearch.Application.Reviews.UpdateReview;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Пересчёт <see cref="BarberProfile.Rating"/> / <see cref="BarberProfile.ReviewsCount"/>
/// при создании, правке, публикации и скрытии отзыва (AGENT.md §12.2).
/// </summary>
public class ReviewRatingTests
{
    private static readonly Guid SalonId = Guid.CreateVersion7();

    [Fact]
    public async Task Create_PublishedReview_UpdatesRatingAndCount()
    {
        var clientId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(clientId, profile.Id, completed: true);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = CreateCreateHandler(bookings, reviews, profiles, clientId);

        var result = await handler.Handle(
            new CreateReviewCommand(booking.Id, 5, "Great haircut"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.00m, profile.Rating);
        Assert.Equal(1, profile.ReviewsCount);
    }

    [Fact]
    public async Task Update_PublishedReview_RecalculatesAverage()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(authorId, profile.Id, completed: true);
        var first = CreateReview(booking, 5);
        var second = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 3);
        var reviews = new FakeReviewRepository();
        reviews.Add(first);
        reviews.Add(second);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(4.00m, 2);

        var handler = CreateUpdateHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new UpdateReviewCommand(first.Id, 1, "Changed my mind"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2.00m, profile.Rating);
        Assert.Equal(2, profile.ReviewsCount);
    }

    [Fact]
    public async Task Publish_HiddenReview_AddsItToSummary()
    {
        var profile = CreateProfile();
        var published = CreateReview(
            CreateBooking(Guid.CreateVersion7(), profile.Id),
            5);
        var hidden = CreateReview(
            CreateBooking(Guid.CreateVersion7(), profile.Id),
            3,
            published: false);
        var reviews = new FakeReviewRepository();
        reviews.Add(published);
        reviews.Add(hidden);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(5.00m, 1);

        var handler = CreatePublishHandler(reviews, profiles);

        var result = await handler.Handle(
            new PublishReviewCommand(hidden.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(hidden.IsPublished);
        Assert.Equal(4.00m, profile.Rating);
        Assert.Equal(2, profile.ReviewsCount);
    }

    [Fact]
    public async Task Hide_PublishedReview_RemovesItFromSummary()
    {
        var profile = CreateProfile();
        var kept = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 5);
        var hidden = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 3);
        var reviews = new FakeReviewRepository();
        reviews.Add(kept);
        reviews.Add(hidden);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(4.00m, 2);

        var handler = CreateHideHandler(reviews, profiles);

        var result = await handler.Handle(
            new HideReviewCommand(hidden.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(hidden.IsPublished);
        Assert.Equal(5.00m, profile.Rating);
        Assert.Equal(1, profile.ReviewsCount);
    }

    [Fact]
    public async Task Update_HiddenReview_DoesNotChangePublicSummary()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var published = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 5);
        var hidden = CreateReview(
            CreateBooking(authorId, profile.Id),
            3,
            published: false);
        var reviews = new FakeReviewRepository();
        reviews.Add(published);
        reviews.Add(hidden);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(5.00m, 1);

        var handler = CreateUpdateHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new UpdateReviewCommand(hidden.Id, 1, "Hidden edit"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, hidden.Rating);
        Assert.Equal(5.00m, profile.Rating);
        Assert.Equal(1, profile.ReviewsCount);
    }

    [Fact]
    public async Task Hide_LastPublishedReview_ClearsRatingAndCount()
    {
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 2);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(2.00m, 1);

        var handler = CreateHideHandler(reviews, profiles);

        var result = await handler.Handle(
            new HideReviewCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(profile.Rating);
        Assert.Equal(0, profile.ReviewsCount);
    }

    [Fact]
    public async Task Publish_ThirdReview_RoundsAverageToTwoDecimals()
    {
        var profile = CreateProfile();
        var reviews = new FakeReviewRepository();
        reviews.Add(CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 5));
        reviews.Add(CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 5));
        var hidden = CreateReview(
            CreateBooking(Guid.CreateVersion7(), profile.Id),
            4,
            published: false);
        reviews.Add(hidden);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(5.00m, 2);

        var handler = CreatePublishHandler(reviews, profiles);

        var result = await handler.Handle(
            new PublishReviewCommand(hidden.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4.67m, profile.Rating);
        Assert.Equal(3, profile.ReviewsCount);
    }

    [Fact]
    public async Task Update_WhenProfileSaveFails_RollsBackReviewChange()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(
            CreateBooking(authorId, profile.Id, completed: true),
            5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(5.00m, 1);
        profiles.UpdateBehavior = _ => UnitResult.Failure<Failure>(new Failure(Error.Failure(
            "barberProfile.update.database",
            "Simulated profile save failure.")));

        var handler = CreateUpdateHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new UpdateReviewCommand(review.Id, 1, "Will be rolled back"),
            CancellationToken.None);

        Assert.True(result.IsFailure);

        var storedReview = (await reviews.GetByIdAsync(review.Id)).Value;
        var storedProfile = (await profiles.GetByIdAsync(profile.Id)).Value;

        Assert.Equal(5, storedReview.Rating);
        Assert.Equal("Original", storedReview.Text);
        Assert.Equal(5.00m, storedProfile.Rating);
        Assert.Equal(1, storedProfile.ReviewsCount);
    }

    private static CreateReviewHandler CreateCreateHandler(
        FakeBookingRepository bookings,
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles,
        Guid clientId)
        => new(
            bookings,
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            new FakeCurrentUser(clientId),
            NullLogger<CreateReviewHandler>.Instance);

    private static UpdateReviewHandler CreateUpdateHandler(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles,
        Guid authorId)
        => new(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            new FakeCurrentUser(authorId),
            NullLogger<UpdateReviewHandler>.Instance);

    private static PublishReviewHandler CreatePublishHandler(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles)
        => new(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            NullLogger<PublishReviewHandler>.Instance);

    private static HideReviewHandler CreateHideHandler(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles)
        => new(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            NullLogger<HideReviewHandler>.Instance);

    private static BarberProfile CreateProfile()
        => BarberProfile.Create(
            Guid.CreateVersion7(),
            "Test Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

    private static Booking CreateBooking(
        Guid clientId,
        Guid barberProfileId,
        bool completed = false)
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(-2);
        var booking = Booking.Create(
            clientId,
            barberProfileId,
            Guid.CreateVersion7(),
            SalonId,
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;

        if (completed)
        {
            booking.Confirm();
            booking.Complete();
        }

        return booking;
    }

    private static Review CreateReview(
        Booking booking,
        int rating,
        bool published = true,
        string text = "Original")
    {
        var review = Review.Create(booking, booking.UserId, rating, text).Value;

        if (!published)
        {
            review.Hide();
        }

        return review;
    }
}
