using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Правило «неявку нельзя отметить раньше начала брони» живёт в Application и сравнивает
/// начало брони с текущим временем из <see cref="TimeProvider"/> (R-27).
/// </summary>
/// <remarks>
/// <para>
/// Фейковые часы стоят в 2025-05-06, то есть в прошлом относительно системного времени.
/// Если вернуть в обработчике <c>DateTimeOffset.UtcNow</c>, первый тест упадёт: системное
/// время уже прошло мимо <c>StartAt</c>, проверка «слишком рано» не сработает, и бронь
/// будет отмечена неявкой.
/// </para>
/// <para>
/// Время передаётся в обработчик через конструктор, поэтому тест не ждёт и не зависит от
/// машины; сдвиг фейковых часов меняет решение обработчика.
/// </para>
/// </remarks>
public class MarkBookingNoShowTests
{
    private static readonly DateTimeOffset FakeNow =
        new(2025, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MarkNoShow_BeforeStart_ReturnsTooEarly()
    {
        var clock = new FakeTimeProvider(FakeNow);
        var booking = CreateConfirmedBooking(clock.UtcNow.AddMinutes(5));
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, clock);

        var result = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.markNoShow.tooEarly", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task MarkNoShow_AfterStart_MarksNoShow()
    {
        var clock = new FakeTimeProvider(FakeNow);
        var booking = CreateConfirmedBooking(clock.UtcNow.AddMinutes(-5));
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, clock);

        var result = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.NoShow, booking.Status);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task MarkNoShow_WhenClockMovesPastStart_BecomesAllowed()
    {
        var clock = new FakeTimeProvider(FakeNow);
        var booking = CreateConfirmedBooking(clock.UtcNow.AddMinutes(5));
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = CreateHandler(bookings, clock);

        var beforeStart = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        // Часы — единственное, что изменилось: тот же обработчик и та же бронь.
        clock.UtcNow = FakeNow.AddMinutes(6);

        var afterStart = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        Assert.True(beforeStart.IsFailure);
        Assert.Equal("booking.markNoShow.tooEarly", beforeStart.Error.Errors[0].Code);
        Assert.True(afterStart.IsSuccess);
        Assert.Equal(BookingStatus.NoShow, booking.Status);
    }

    private static MarkBookingNoShowHandler CreateHandler(
        FakeBookingRepository bookings,
        TimeProvider clock)
        => new(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            clock,
            NullLogger<MarkBookingNoShowHandler>.Instance);

    private static Booking CreateConfirmedBooking(DateTimeOffset startAt)
    {
        var booking = Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;

        Assert.True(booking.Confirm().IsSuccess);

        return booking;
    }
}
