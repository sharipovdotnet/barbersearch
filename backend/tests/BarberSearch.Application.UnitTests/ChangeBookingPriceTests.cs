using BarberSearch.Application.Bookings.ChangeBookingPrice;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Правка цены визита: одно поле, права владельца профиля или администратора и коды
/// «поле не передано» / «нет брони» / «статус заморозил цену».
/// </summary>
/// <remarks>
/// Отсутствующая цена проверяется до обращения к репозиторию: <c>decimal?</c> в контракте
/// существует ровно для того, чтобы пропущенное поле не стало валидным нулём (D5).
/// </remarks>
public class ChangeBookingPriceTests
{
    [Fact]
    public async Task ChangePrice_WithoutPrice_ReturnsRequiredAndDoesNotTouchRepository()
    {
        var booking = CreateBooking(Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.price.required", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Empty(bookings.Operations);
        Assert.Equal(1500m, booking.Price);
    }

    [Fact]
    public async Task ChangePrice_ByAdmin_Succeeds()
    {
        var booking = CreateBooking(Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, 2000m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000m, booking.Price);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task ChangePrice_ByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = CreateHandler(bookings, new FakeCurrentUser(barberId), profiles);

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, 2000m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000m, booking.Price);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task ChangePrice_ByStranger_ReturnsForbidden()
    {
        var booking = CreateBooking(Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, 2000m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(1500m, booking.Price);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task ChangePrice_ForUnknownBooking_ReturnsNotFound()
    {
        var bookings = new FakeBookingRepository();
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(Guid.CreateVersion7(), 2000m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.notFound", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task ChangePrice_OfCancelledBooking_ReturnsPriceStatusAndDoesNotSave()
    {
        var booking = CreateBooking(Guid.CreateVersion7());
        Assert.True(booking.Cancel().IsSuccess);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, 2000m),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.price.status", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(1500m, booking.Price);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task ChangePrice_ToTheSameValue_SucceedsWithoutFailure()
    {
        var booking = CreateBooking(Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new ChangeBookingPriceCommand(booking.Id, 1500m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, booking.Price);
    }

    private static ChangeBookingPriceHandler CreateHandler(
        FakeBookingRepository bookings,
        FakeCurrentUser currentUser,
        FakeBarberProfileRepository? profiles = null)
        => new(
            bookings,
            profiles ?? new FakeBarberProfileRepository(),
            currentUser,
            NullLogger<ChangeBookingPriceHandler>.Instance);

    private static Booking CreateBooking(Guid barberProfileId)
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            Guid.CreateVersion7(),
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
}
