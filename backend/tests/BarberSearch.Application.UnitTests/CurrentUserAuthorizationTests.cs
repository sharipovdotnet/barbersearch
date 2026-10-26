using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.Common;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Анонимный запрос не бросает исключение и не подставляет <see cref="Guid.Empty"/>:
/// обработчики и проверки владения возвращают <c>401 auth.unauthorized</c> (R-29).
/// </summary>
public class CurrentUserAuthorizationTests
{
    [Fact]
    public void RequireUserId_Anonymous_ReturnsUnauthorized()
    {
        var result = FakeCurrentUser.Anonymous().RequireUserId();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    [Fact]
    public void RequireUserId_Authenticated_ReturnsUserId()
    {
        var userId = Guid.CreateVersion7();

        var result = new FakeCurrentUser(userId).RequireUserId();

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value);
    }

    [Fact]
    public void EnsureCanManage_Anonymous_ReturnsUnauthorized()
    {
        var result = FakeCurrentUser.Anonymous().EnsureCanManage(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task EnsureIsBarberOfAsync_Anonymous_ReturnsUnauthorized()
    {
        var result = await FakeCurrentUser.Anonymous().EnsureIsBarberOfAsync(
            new FakeBarberProfileRepository(),
            Guid.CreateVersion7(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task CancelBooking_Anonymous_ReturnsUnauthorizedAndWritesNothing()
    {
        var booking = CreateBooking();
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            FakeCurrentUser.Anonymous(),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public async Task CreateBooking_Anonymous_ReturnsUnauthorizedAndWritesNothing()
    {
        var bookings = new FakeBookingRepository();
        var profiles = new FakeBarberProfileRepository();
        var handler = new CreateBookingHandler(
            bookings,
            profiles,
            new FakeSalonRepository(),
            new FakeUnitOfWork(),
            FakeCurrentUser.Anonymous(),
            TimeProvider.System,
            NullLogger<CreateBookingHandler>.Instance);

        var startAt = DateTimeOffset.UtcNow.AddHours(1);
        var request = new CreateBookingRequest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddHours(1),
            $"key-{Guid.NewGuid()}",
            null);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.LockCalls);
        Assert.Equal(0, bookings.GetByIdempotencyKeyCalls);
        Assert.Equal(0, bookings.AddCalls);
    }

    private static Booking CreateBooking()
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;
    }
}
