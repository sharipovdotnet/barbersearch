using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;

namespace BarberSearch.Domain.Reviews;

public class Review : AggregateRoot
{
    public Guid BarberProfileId { get; private set; }
    public Guid UserId { get; private set; }

    public Guid? BookingId { get; private set; }

    public int Rating { get; private set; }

    public string? Text { get; private set; }

    public bool IsPublished { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private Review()
    {
    }

    public Review(
        Guid barberProfileId,
        Guid userId,
        int rating,
        string? text,
        Guid? bookingId = null)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating));

        BarberProfileId = barberProfileId;
        UserId = userId;
        BookingId = bookingId;

        Rating = rating;
        Text = text;

        IsPublished = true;
    }

    public void Update(int rating, string? text)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating));

        Rating = rating;
        Text = text;

        MarkUpdated();
    }

    public void Hide()
    {
        IsPublished = false;
        MarkUpdated();
    }
}