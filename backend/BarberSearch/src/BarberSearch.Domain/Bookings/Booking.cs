using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;

namespace BarberSearch.Domain.Bookings;

public class Booking : AggregateRoot
{
    public Guid UserId { get; private set; }
    public Guid BarberProfileId { get; private set; }
    public Guid BarberServiceId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }

    public decimal Price { get; private set; }

    public BookingStatus Status { get; private set; }

    public string? CustomerNote { get; private set; }
    public string? BarberNote { get; private set; }

    public User User { get; private set; } = null!;
    public BarberProfile BarberProfile { get; private set; } = null!;
    public BarberService BarberService { get; private set; } = null!;

    private Booking()
    {
    }

    public Booking(
        Guid userId,
        Guid barberProfileId,
        Guid barberServiceId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        decimal price,
        string? customerNote = null)
    {
        if (endAt <= startAt)
            throw new ArgumentException(
                "End time must be after start time.");

        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price));

        UserId = userId;
        BarberProfileId = barberProfileId;
        BarberServiceId = barberServiceId;

        StartAt = startAt;
        EndAt = endAt;

        Price = price;
        CustomerNote = customerNote;

        Status = BookingStatus.Pending;
    }

    public void Confirm()
    {
        if (Status != BookingStatus.Pending)
            throw new InvalidOperationException(
                "Only pending bookings can be confirmed.");

        Status = BookingStatus.Confirmed;
        MarkUpdated();
    }

    public void Cancel()
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
            throw new InvalidOperationException(
                "Booking cannot be cancelled.");

        Status = BookingStatus.Cancelled;
        MarkUpdated();
    }

    public void Complete()
    {
        if (Status != BookingStatus.Confirmed)
            throw new InvalidOperationException(
                "Only confirmed bookings can be completed.");

        Status = BookingStatus.Completed;
        MarkUpdated();
    }

    public void AddBarberNote(string note)
    {
        BarberNote = note;
        MarkUpdated();
    }
}