using BarberSearch.Application.Reviews;
using BarberSearch.Application.Reviews.CreateReview;
using BarberSearch.Application.Reviews.DeleteReview;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Удаление своего отзыва: владение, пересчёт рейтинга опубликованного отзыва
/// и освобождение брони для нового отзыва.
/// </summary>
public class DeleteReviewTests
{
    private static readonly Guid SalonId = Guid.CreateVersion7();

    [Fact]
    public async Task Delete_HiddenReview_KeepsPublicSummary()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var hidden = CreateReview(
            CreateBooking(authorId, profile.Id),
            3,
            published: false);
        var published = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 2);
        var reviews = new FakeReviewRepository();
        reviews.Add(hidden);
        reviews.Add(published);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        // Сводка намеренно расходится с опубликованным отзывом (5.00 при отзыве на 2):
        // так видно, что удаление скрытого отзыва пересчёт не вызывает вовсе.
        profile.UpdateRating(5.00m, 1);

        var handler = CreateDeleteHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new DeleteReviewCommand(hidden.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(reviews.Reviews, review => review.Id == hidden.Id);
        Assert.Equal(5.00m, profile.Rating);
        Assert.Equal(1, profile.ReviewsCount);
    }

    [Fact]
    public async Task Delete_PublishedReview_RecalculatesAverageAndCount()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var own = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var kept = CreateReview(CreateBooking(Guid.CreateVersion7(), profile.Id), 4);
        var reviews = new FakeReviewRepository();
        reviews.Add(own);
        reviews.Add(kept);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(4.50m, 2);

        var handler = CreateDeleteHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new DeleteReviewCommand(own.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(reviews.Reviews, review => review.Id == own.Id);
        Assert.Equal(4.00m, profile.Rating);
        Assert.Equal(1, profile.ReviewsCount);
    }

    [Fact]
    public async Task Delete_LastPublishedReview_ClearsRatingAndCount()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        profile.UpdateRating(5.00m, 1);

        var handler = CreateDeleteHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new DeleteReviewCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(profile.Rating);
        Assert.Equal(0, profile.ReviewsCount);
    }

    [Fact]
    public async Task Delete_ForeignReview_IsForbiddenAndKeepsRow()
    {
        var authorId = Guid.CreateVersion7();
        var friendId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = CreateDeleteHandler(reviews, profiles, friendId);

        var result = await handler.Handle(
            new DeleteReviewCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.Forbidden, result.Error.Errors[0].Code);
        Assert.Contains(reviews.Reviews, candidate => candidate.Id == review.Id);
    }

    [Fact]
    public async Task Delete_AsAdmin_IsForbiddenBecauseAdminsHideInstead()
    {
        var authorId = Guid.CreateVersion7();
        var adminId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = CreateDeleteHandler(reviews, profiles, adminId, isAdmin: true);

        var result = await handler.Handle(
            new DeleteReviewCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Contains(reviews.Reviews, candidate => candidate.Id == review.Id);
    }

    [Fact]
    public async Task Delete_UnknownReview_ReturnsNotFound()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = CreateDeleteHandler(reviews, profiles, authorId);

        var result = await handler.Handle(
            new DeleteReviewCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Review.NotFound, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Delete_SecondTime_ReturnsNotFound()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = CreateDeleteHandler(reviews, profiles, authorId);
        var command = new DeleteReviewCommand(review.Id);

        Assert.True((await handler.Handle(command, CancellationToken.None)).IsSuccess);

        var repeated = await handler.Handle(command, CancellationToken.None);

        Assert.True(repeated.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, repeated.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Review.NotFound, repeated.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Delete_ForAnonymous_ReturnsUnauthorized()
    {
        var authorId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var review = CreateReview(CreateBooking(authorId, profile.Id), 5);
        var reviews = new FakeReviewRepository();
        reviews.Add(review);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var handler = new DeleteReviewHandler(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            FakeCurrentUser.Anonymous(),
            NullLogger<DeleteReviewHandler>.Instance);

        var result = await handler.Handle(
            new DeleteReviewCommand(review.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.Unauthorized, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Delete_FreesBookingForNewReview()
    {
        var clientId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var booking = CreateBooking(clientId, profile.Id, completed: true);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var reviews = new FakeReviewRepository();
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var createHandler = new CreateReviewHandler(
            bookings,
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new FakeCurrentUser(clientId),
            NullLogger<CreateReviewHandler>.Instance);

        var first = await createHandler.Handle(
            new CreateReviewCommand(booking.Id, 5, "First"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False((await createHandler.Handle(
            new CreateReviewCommand(booking.Id, 4, "Second"),
            CancellationToken.None)).IsSuccess);

        var deleteHandler = CreateDeleteHandler(reviews, profiles, clientId);
        Assert.True((await deleteHandler.Handle(
            new DeleteReviewCommand(first.Value),
            CancellationToken.None)).IsSuccess);

        // Уникальный индекс по брони освобождён: автор может оставить отзыв снова.
        var second = await createHandler.Handle(
            new CreateReviewCommand(booking.Id, 4, "Second"),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value, second.Value);
    }

    private static DeleteReviewHandler CreateDeleteHandler(
        FakeReviewRepository reviews,
        FakeBarberProfileRepository profiles,
        Guid currentUserId,
        bool isAdmin = false)
        => new(
            reviews,
            new FakeUnitOfWork(reviews, profiles),
            new ReviewRatingRecalculator(profiles, reviews),
            new FakeCurrentUser(currentUserId, isAdmin),
            NullLogger<DeleteReviewHandler>.Instance);

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
        bool published = true)
    {
        var review = Review.Create(booking, booking.UserId, rating, "Original").Value;

        // R-35: Create оставляет отзыв скрытым; опубликованные для тестов включаем явно.
        if (published)
        {
            review.Publish();
        }

        return review;
    }
}
