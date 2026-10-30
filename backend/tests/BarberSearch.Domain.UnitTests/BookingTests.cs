using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class BookingTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static Booking CreateValidBooking(string idempotencyKey = "key-1")
        => Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            Start.AddMinutes(45),
            1500m,
            idempotencyKey).Value;

    [Fact]
    public void Create_WithValidData_StartsPending()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Start,
            Start.AddMinutes(45),
            1500m,
            "key-1",
            "Please be on time");

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Pending, result.Value.Status);
        Assert.Equal("key-1", result.Value.IdempotencyKey);
        Assert.Equal("Please be on time", result.Value.CustomerNote);
    }

    [Fact]
    public void Create_WithSalonId_KeepsSalon()
    {
        var salonId = Guid.CreateVersion7();

        var booking = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), salonId,
            Start, Start.AddMinutes(30), 1000m, "key-1").Value;

        Assert.Equal(salonId, booking.SalonId);
    }

    [Fact]
    public void Create_WithEndBeforeStart_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start, Start.AddMinutes(-1), 1000m, "key-1");

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.period", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithEqualStartAndEnd_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start, Start, 1000m, "key-1");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithMaximumDuration_Succeeds()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start,
            Start.AddMinutes(Constants.MAX_BOOKING_DURATION_MINUTES),
            1000m,
            "key-1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithDurationLongerThanMaximum_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start,
            Start.AddMinutes(Constants.MAX_BOOKING_DURATION_MINUTES + 1),
            1000m,
            "key-1");

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.period.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithNegativePrice_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start, Start.AddMinutes(30), -1m, "key-1");

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.price", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithPaddedIdempotencyKey_StoresItTrimmed()
    {
        var booking = CreateValidBooking("  key-1  ");

        Assert.Equal("key-1", booking.IdempotencyKey);
    }

    [Fact]
    public void Create_WithoutIdempotencyKey_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start, Start.AddMinutes(30), 1000m, "  ");

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.idempotencyKey", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithTooLongIdempotencyKey_Fails()
    {
        var result = Booking.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Start, Start.AddMinutes(30), 1000m, new string('k', 200));

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.idempotencyKey.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Confirm_FromPending_Succeeds()
    {
        var booking = CreateValidBooking();

        var result = booking.Confirm();

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Confirm_FromConfirmed_ReturnsInvalidStatus()
    {
        var booking = CreateValidBooking();
        booking.Confirm();

        var result = booking.Confirm();

        Assert.True(result.IsFailure);
        Assert.Equal("booking.confirm.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
    }

    [Fact]
    public void Complete_RequiresConfirmedStatus()
    {
        var booking = CreateValidBooking();

        var fromPending = booking.Complete();

        Assert.True(fromPending.IsFailure);
        Assert.Equal("booking.complete.invalidStatus", fromPending.Error.Errors[0].Code);

        booking.Confirm();
        var fromConfirmed = booking.Complete();

        Assert.True(fromConfirmed.IsSuccess);
        Assert.Equal(BookingStatus.Completed, booking.Status);
    }

    [Fact]
    public void Cancel_IsAllowedFromPendingAndConfirmed()
    {
        var pending = CreateValidBooking();
        Assert.True(pending.Cancel().IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, pending.Status);

        var confirmed = CreateValidBooking();
        confirmed.Confirm();
        Assert.True(confirmed.Cancel().IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, confirmed.Status);
    }

    [Fact]
    public void Cancel_FromCompleted_ReturnsInvalidStatus()
    {
        var booking = CreateValidBooking();
        booking.Confirm();
        booking.Complete();

        var result = booking.Cancel();

        Assert.True(result.IsFailure);
        Assert.Equal("booking.cancel.invalidStatus", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Cancel_FromCancelled_ReturnsInvalidStatus()
    {
        var booking = CreateValidBooking();
        booking.Cancel();

        var result = booking.Cancel();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkNoShow_FromConfirmed_Succeeds()
    {
        var booking = CreateValidBooking();
        booking.Confirm();

        var result = booking.MarkNoShow();

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.NoShow, booking.Status);
    }

    [Fact]
    public void MarkNoShow_FromPending_ReturnsInvalidStatus()
    {
        var booking = CreateValidBooking();

        var result = booking.MarkNoShow();

        Assert.True(result.IsFailure);
        Assert.Equal("booking.markNoShow.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public void AddBarberNote_KeepsNoteAndUpdatesTimestamp()
    {
        var booking = CreateValidBooking();
        var before = booking.UpdatedAt;

        var result = booking.AddBarberNote("Client is late");

        Assert.True(result.IsSuccess);
        Assert.Equal("Client is late", booking.BarberNote);
        Assert.True(booking.UpdatedAt >= before);
    }

    [Fact]
    public void AddBarberNote_TrimsEdgesAndTreatsBlankAsNoNote()
    {
        var booking = CreateValidBooking();

        Assert.True(booking.AddBarberNote("  Client is late  ").IsSuccess);
        Assert.Equal("Client is late", booking.BarberNote);

        Assert.True(booking.AddBarberNote("   ").IsSuccess);
        Assert.Null(booking.BarberNote);

        Assert.True(booking.AddBarberNote(null).IsSuccess);
        Assert.Null(booking.BarberNote);
    }

    [Fact]
    public void AddBarberNote_WithMaxLength_Succeeds()
    {
        var booking = CreateValidBooking();
        var note = new string('a', Constants.MAX_BARBER_NOTE_LENGTH);

        var result = booking.AddBarberNote(note);

        Assert.True(result.IsSuccess);
        Assert.Equal(note, booking.BarberNote);
    }

    [Fact]
    public void AddBarberNote_WithTooLongNote_ReturnsValidationAndKeepsPreviousNote()
    {
        var booking = CreateValidBooking();
        Assert.True(booking.AddBarberNote("Previous").IsSuccess);

        var result = booking.AddBarberNote(new string('a', Constants.MAX_BARBER_NOTE_LENGTH + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("booking.barberNote.tooLong", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("Previous", booking.BarberNote);
    }

    [Fact]
    public void EveryBookingStatus_IsReachable()
    {
        var confirmed = CreateValidBooking();
        confirmed.Confirm();

        var completed = CreateValidBooking();
        completed.Confirm();
        completed.Complete();

        var cancelled = CreateValidBooking();
        cancelled.Cancel();

        var noShow = CreateValidBooking();
        noShow.Confirm();
        noShow.MarkNoShow();

        Assert.Equal(
            new[]
            {
                BookingStatus.Pending,
                BookingStatus.Confirmed,
                BookingStatus.Completed,
                BookingStatus.Cancelled,
                BookingStatus.NoShow
            },
            new[]
            {
                CreateValidBooking().Status,
                confirmed.Status,
                completed.Status,
                cancelled.Status,
                noShow.Status
            });
    }
}
