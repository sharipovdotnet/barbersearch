using BarberSearch.Application.Reviews;
using BarberSearch.Application.Reviews.CreateReview;
using BarberSearch.Application.Reviews.UpdateReview;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class ReviewOwnershipTests
{
    private static readonly Guid SalonId = Guid.CreateVersion7();

    private static Booking CreateBooking(
        Guid clientId,
        Guid barberProfileId,
        bool completed)
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

    private static CreateReviewHandler CreateCreateHandler(
        FakeBookingRepository bookings,
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles,
        FakeCurrentUser currentUser)
        => new(
            bookings,
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            currentUser,
            NullLogger<CreateReviewHandler>.Instance);

    private static UpdateReviewHandler CreateUpdateHandler(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles,
        FakeCurrentUser currentUser)
        => new(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            currentUser,
            NullLogger<UpdateReviewHandler>.Instance);

    [Fact]
    public async Task Create_ByClientOfCompletedBooking_TakesAuthorFromToken()
    {
        var clientId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(clientId, profile.Id, completed: true);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateCreateHandler(
            bookings,
            reviews,
            profiles,
            new FakeCurrentUser(clientId));

        var result = await handler.Handle(
            new CreateReviewCommand(booking.Id, 5, "Great haircut"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var review = Assert.Single(reviews.Reviews);
        Assert.Equal(clientId, review.UserId);
        Assert.Equal(booking.BarberProfileId, review.BarberProfileId);
        Assert.Equal(result.Value, review.Id);
    }

    [Fact]
    public async Task Create_ByStranger_ReturnsForbidden()
    {
        var profile = CreateProfile();
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id, completed: true);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateCreateHandler(
            bookings,
            reviews,
            profiles,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new CreateReviewCommand(booking.Id, 5, "Fake review"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Empty(reviews.Reviews);
    }

    [Fact]
    public async Task Create_WhenBookingNotCompleted_ReturnsConflict()
    {
        var clientId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(clientId, profile.Id, completed: false);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateCreateHandler(
            bookings,
            new FakeReviewRepository(),
            profiles,
            new FakeCurrentUser(clientId));

        var result = await handler.Handle(
            new CreateReviewCommand(booking.Id, 5, "Too early"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("review.create.bookingNotCompleted", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Create_ByAdmin_ReturnsForbidden()
    {
        var profile = CreateProfile();
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id, completed: true);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateCreateHandler(
            bookings,
            new FakeReviewRepository(),
            profiles,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new CreateReviewCommand(booking.Id, 5, "Admin review"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Update_ByAuthor_Succeeds()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(authorId, profile.Id, completed: true);
        var review = Review.Create(booking, authorId, 5, "Old text").Value;
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateUpdateHandler(
            reviews,
            profiles,
            new FakeCurrentUser(authorId));

        var result = await handler.Handle(
            new UpdateReviewCommand(review.Id, 3, "New text"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, review.Rating);
        Assert.Equal("New text", review.Text);
    }

    [Fact]
    public async Task Update_ByStranger_ReturnsForbidden()
    {
        var profile = CreateProfile();
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id, completed: true);
        var review = Review.Create(booking, booking.UserId, 5, "Text").Value;
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateUpdateHandler(
            reviews,
            profiles,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new UpdateReviewCommand(review.Id, 1, "Vandalized"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(5, review.Rating);
    }

    [Fact]
    public async Task Update_ByAdmin_Succeeds()
    {
        var profile = CreateProfile();
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id, completed: true);
        var review = Review.Create(booking, booking.UserId, 5, "Text").Value;
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateUpdateHandler(
            reviews,
            profiles,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new UpdateReviewCommand(review.Id, 4, "Moderated"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_WhenReviewMissing_ReturnsNotFound()
    {
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        var handler = CreateUpdateHandler(
            reviews,
            profiles,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new UpdateReviewCommand(Guid.CreateVersion7(), 4, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
    }
}
