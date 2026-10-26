using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.CompleteBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Проигранная оптимистическая блокировка брони (R-05): репозиторий вернул
/// <c>booking.update.concurrencyConflict</c>, обработчик не должен превращать это в успех.
/// </summary>
/// <remarks>
/// Настоящую гонку двух <c>DbContext</c> воспроизводит только PostgreSQL: <c>xmin</c> в
/// <c>WHERE</c> не совпадает у проигравшего. Здесь проверяется вторая половина контракта —
/// что 409 доходит до клиента с кодом <c>concurrencyConflict</c>, а не теряется в обработчике.
/// </remarks>
public class BookingConcurrencyTests
{
    private const string ConcurrencyCode = "booking.update.concurrencyConflict";

    [Fact]
    public async Task Cancel_WhenBookingWasModifiedConcurrently_ReturnsConflict()
    {
        var clientId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        Assert.True(booking.Confirm().IsSuccess);

        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        bookings.OnUpdate = ConcurrencyConflict;
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(clientId),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(ConcurrencyCode, result.Error.Errors[0].Code);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Complete_WhenBookingWasModifiedConcurrently_ReturnsConflict()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        Assert.True(booking.Confirm().IsSuccess);

        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        bookings.OnUpdate = ConcurrencyConflict;
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new CompleteBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            NullLogger<CompleteBookingHandler>.Instance);

        var result = await handler.Handle(
            new CompleteBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(ConcurrencyCode, result.Error.Errors[0].Code);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    private static UnitResult<Failure> ConcurrencyConflict(Booking booking)
        => UnitResult.Failure<Failure>(new Failure(Error.Conflict(
            ConcurrencyCode,
            $"Booking '{booking.Id}' was modified by another request. Reload it and retry.")));

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
}
