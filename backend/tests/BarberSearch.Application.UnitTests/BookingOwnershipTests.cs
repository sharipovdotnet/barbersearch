using BarberSearch.Application.Bookings.AddBookingBarberNote;
using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.ConfirmBooking;
using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class BookingOwnershipTests
{
    private static Booking CreateBooking(Guid clientId, Guid barberProfileId)
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            clientId,
            barberProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;
    }

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

    [Fact]
    public async Task Confirm_ByClient_ReturnsForbidden()
    {
        var clientId = Guid.CreateVersion7();
        var barberId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(CreateProfile(barberId));
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(clientId),
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public async Task Confirm_ByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task Confirm_ByAdmin_Succeeds()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new ConfirmBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Cancel_ByClient_Succeeds()
    {
        var clientId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(clientId),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByStranger_ReturnsForbidden()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public async Task MarkNoShow_ByStranger_ReturnsForbiddenBeforeTimeRule()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new MarkBookingNoShowHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<MarkBookingNoShowHandler>.Instance);

        var result = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task AddBarberNote_ByClient_ReturnsForbidden()
    {
        var clientId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new AddBookingBarberNoteHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(clientId),
            NullLogger<AddBookingBarberNoteHandler>.Instance);

        var result = await handler.Handle(
            new AddBookingBarberNoteCommand(booking.Id, "Secret note"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Null(booking.BarberNote);
    }
}
