using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Слоты: границы дня в таймзоне салона, сетка, отпуска, активные и отменённые брони.
/// </summary>
public class BarberSlotsTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid SalonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Date = new(2026, 12, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Slots_UseSalonTimeZoneAndExplicitDayBounds()
    {
        var handler = CreateHandler(CreateContext());

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Europe/Berlin", result.Value.TimeZoneId);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero), result.Value.DayStart);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 23, 0, 0, TimeSpan.Zero), result.Value.DayEnd);
        Assert.Equal(18, result.Value.Slots.Count);
        Assert.Null(result.Value.BarberServiceId);
        Assert.Null(result.Value.HairServiceId);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 17, 0, 0, TimeSpan.Zero),
            result.Value.Slots[^1].EndAt);
    }

    [Fact]
    public async Task Slots_ExcludeTimeOffAndKeepTouchingSlots()
    {
        var timeOffs = new[]
        {
            new TimeOffInterval(
                new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero))
        };
        var handler = CreateHandler(CreateContext(timeOffs: timeOffs));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(14, result.Value.Slots.Count);
        Assert.DoesNotContain(
            result.Value.Slots,
            slot => slot.StartAt < timeOffs[0].EndAt && timeOffs[0].StartAt < slot.EndAt);
        Assert.Contains(
            result.Value.Slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero));
        Assert.Contains(
            result.Value.Slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Slots_ActiveBookingBlocksButCancelledDoesNot()
    {
        var bookings = new FakeBookingRepository();
        var confirmed = CreateBooking(
            new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));
        confirmed.Confirm();
        bookings.Add(confirmed);

        var cancelled = CreateBooking(
            new DateTimeOffset(2026, 12, 1, 10, 30, 0, TimeSpan.Zero));
        cancelled.Cancel();
        bookings.Add(cancelled);

        var handler = CreateHandler(CreateContext(), bookings);

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            result.Value.Slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains(
            result.Value.Slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 10, 30, 0, TimeSpan.Zero));
        Assert.Equal(17, result.Value.Slots.Count);
    }

    [Fact]
    public async Task Slots_DayOff_ReturnEmptySlotsButExplicitBounds()
    {
        var handler = CreateHandler(CreateContext(isWorkingDay: false));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsWorkingDay);
        Assert.Empty(result.Value.Slots);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero), result.Value.DayStart);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 23, 0, 0, TimeSpan.Zero), result.Value.DayEnd);
    }

    [Fact]
    public async Task Slots_ServiceDuration_ControlsGridStep()
    {
        var handler = CreateHandler(CreateContext(durationMinutes: 45));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, Guid.CreateVersion7(), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, result.Value.DurationMinutes);
        Assert.Equal(12, result.Value.Slots.Count);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 8, 45, 0, TimeSpan.Zero),
            result.Value.Slots[1].StartAt);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 16, 15, 0, TimeSpan.Zero),
            result.Value.Slots[^1].StartAt);
    }

    [Fact]
    public async Task Slots_WithHairService_ReturnThePairUsedForDuration()
    {
        var barberServiceId = Guid.CreateVersion7();
        var hairServiceId = Guid.CreateVersion7();
        var handler = CreateHandler(CreateContext(
            durationMinutes: 45,
            barberServiceId: barberServiceId,
            hairServiceId: hairServiceId));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, hairServiceId, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(barberServiceId, result.Value.BarberServiceId);
        Assert.Equal(hairServiceId, result.Value.HairServiceId);
        Assert.Equal(45, result.Value.DurationMinutes);
        Assert.Equal(12, result.Value.Slots.Count);
    }

    [Fact]
    public async Task Slots_InThePast_AreNotOffered()
    {
        var handler = CreateHandler(
            CreateContext(),
            now: new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 12, 30, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
    }

    [Fact]
    public async Task Slots_WithoutDate_ReturnsValidation()
    {
        var handler = CreateHandler(CreateContext());

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Slots_WithoutProfile_ReturnsNotFound()
    {
        var handler = new GetBarberSlotsHandler(
            new FakeBarberProfileRepository(),
            new FakeBookingRepository(),
            new FakeTimeProvider(Now),
            NullLogger<GetBarberSlotsHandler>.Instance);

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Slots_WithUnresolvableTimeZone_ReturnsConflictNotServerError()
    {
        var handler = CreateHandler(CreateContext(timeZoneId: "Not/AZone"));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("barberSlots.timeZone.unknown", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Slots_WithoutTimeZone_ReturnsDistinctConflict()
    {
        var handler = CreateHandler(CreateContext(timeZoneId: " "));

        var result = await handler.Handle(
            new GetBarberSlotsQuery(BarberProfileId, Date, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("barberSlots.timeZone.missing", result.Error.Errors[0].Code);
    }

    private static GetBarberSlotsHandler CreateHandler(
        BarberSlotContext context,
        FakeBookingRepository? bookings = null,
        DateTimeOffset? now = null)
        => new(
            new FakeBarberProfileRepository { SlotContext = context },
            bookings ?? new FakeBookingRepository(),
            new FakeTimeProvider(now ?? Now),
            NullLogger<GetBarberSlotsHandler>.Instance);

    private static BarberSlotContext CreateContext(
        int durationMinutes = 30,
        bool isWorkingDay = true,
        IReadOnlyList<TimeOffInterval>? timeOffs = null,
        string timeZoneId = "Europe/Berlin",
        Guid? barberServiceId = null,
        Guid? hairServiceId = null)
        => new(
            BarberProfileId,
            SalonId,
            timeZoneId,
            Date,
            isWorkingDay,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            durationMinutes,
            barberServiceId,
            hairServiceId,
            timeOffs ?? []);

    private static Booking CreateBooking(DateTimeOffset startAt)
        => Booking.Create(
            Guid.CreateVersion7(),
            BarberProfileId,
            Guid.CreateVersion7(),
            SalonId,
            startAt,
            startAt.AddMinutes(30),
            30m,
            $"key-{Guid.NewGuid()}").Value;
}
