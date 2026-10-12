using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class ReviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static Booking CreateBooking(Guid? barberProfileId = null)
        => Booking.Create(
            Guid.CreateVersion7(),
            barberProfileId ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            Start.AddMinutes(45),
            1500m,
            "key-1").Value;

    [Fact]
    public void Create_TakesBarberFromBooking()
    {
        var barberProfileId = Guid.CreateVersion7();
        var booking = CreateBooking(barberProfileId);
        var userId = Guid.CreateVersion7();

        var review = Review.Create(booking, userId, 5, "Great!");

        Assert.True(review.IsSuccess);
        Assert.Equal(barberProfileId, review.Value.BarberProfileId);
        Assert.Equal(userId, review.Value.UserId);
        Assert.Equal(booking.Id, review.Value.BookingId);
    }

    [Fact]
    public void Create_AlwaysHasBookingId()
    {
        var booking = CreateBooking();

        var review = Review.Create(booking, Guid.CreateVersion7(), 4, null);

        Assert.True(review.IsSuccess);
        Assert.NotEqual(Guid.Empty, review.Value.BookingId);
    }

    [Fact]
    public void Create_StartsPublished()
    {
        var review = Review.Create(CreateBooking(), Guid.CreateVersion7(), 5, null);

        Assert.True(review.Value.IsPublished);
    }

    [Fact]
    public void Create_WithRatingAboveFive_Fails()
    {
        var result = Review.Create(CreateBooking(), Guid.CreateVersion7(), 6, null);

        Assert.True(result.IsFailure);
        Assert.Equal("review.rating", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithRatingBelowOne_Fails()
    {
        var result = Review.Create(CreateBooking(), Guid.CreateVersion7(), 0, null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Update_WithInvalidRating_KeepsOldValue()
    {
        var review = Review.Create(CreateBooking(), Guid.CreateVersion7(), 5, "Great!").Value;

        var result = review.Update(9, "Bad!");

        Assert.True(result.IsFailure);
        Assert.Equal(5, review.Rating);
        Assert.Equal("Great!", review.Text);
    }

    [Fact]
    public void Update_WithValidData_ChangesState()
    {
        var review = Review.Create(CreateBooking(), Guid.CreateVersion7(), 5, "Great!").Value;

        var result = review.Update(3, "Average");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, review.Rating);
        Assert.Equal("Average", review.Text);
    }

    [Fact]
    public void HideThenPublish_TogglesVisibility()
    {
        var review = Review.Create(CreateBooking(), Guid.CreateVersion7(), 5, null).Value;

        Assert.True(review.Hide().IsSuccess);
        Assert.False(review.IsPublished);

        Assert.True(review.Publish().IsSuccess);
        Assert.True(review.IsPublished);
    }

    [Fact]
    public void Publish_IsIdempotent()
    {
        var review = Review.Create(CreateBooking(), Guid.CreateVersion7(), 5, null).Value;

        Assert.True(review.Publish().IsSuccess);
        Assert.True(review.Publish().IsSuccess);
        Assert.True(review.IsPublished);
    }
}
