using System.Text.Json;
using BarberSearch.Application.Bookings.GetBarberBookings;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Domain.Bookings;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Записи барбера: скоупы, порядок, граница по времени, курсор в обе стороны, права и PII.
/// </summary>
public class BarberBookingReadTests
{
    private static readonly Guid BarberProfileId = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Опции сериализации в стиле API: имена полей в camelCase.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Upcoming_ReturnsOnlyActiveBookingsWithFutureEndAscending()
    {
        var bookings = new FakeBookingRepository();
        var first = CreateBooking(Now.AddHours(2), BookingStatus.Confirmed);
        var second = CreateBooking(Now.AddHours(4), BookingStatus.Pending);
        bookings.Add(second);
        bookings.Add(first);
        bookings.Add(CreateBooking(Now.AddHours(1), BookingStatus.Completed));
        bookings.Add(CreateBooking(Now.AddHours(3), BookingStatus.Cancelled));
        bookings.Add(CreateBooking(Now.AddHours(5), BookingStatus.NoShow));

        var result = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery("upcoming", null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { first.Id, second.Id }, result.Value.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Upcoming_ExcludesActiveBookingWhoseEndHasPassed()
    {
        var bookings = new FakeBookingRepository();
        var running = CreateBooking(Now.AddMinutes(-10), BookingStatus.Confirmed);
        var endedNow = CreateBooking(Now.AddMinutes(-60), BookingStatus.Confirmed);
        bookings.Add(running);
        bookings.Add(endedNow);

        var result = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(running.Id, item.Id);
    }

    [Fact]
    public async Task Past_ReturnsTerminalAndExpiredBookingsNewestFirst()
    {
        var bookings = new FakeBookingRepository();
        var recentRunning = CreateBooking(Now.AddMinutes(-120), BookingStatus.Confirmed);
        var completed = CreateBooking(Now.AddDays(-3), BookingStatus.Completed);
        var cancelled = CreateBooking(Now.AddDays(-4), BookingStatus.Cancelled);
        var noShow = CreateBooking(Now.AddDays(-2), BookingStatus.NoShow);
        bookings.Add(completed);
        bookings.Add(cancelled);
        bookings.Add(noShow);
        bookings.Add(recentRunning);
        bookings.Add(CreateBooking(Now.AddHours(6), BookingStatus.Confirmed));
        bookings.Add(CreateBooking(Now.AddHours(8), BookingStatus.Pending));

        var result = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery("past", null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { recentRunning.Id, noShow.Id, completed.Id, cancelled.Id },
            result.Value.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Page_ReturnsOnlyBookingsOfTheCurrentBarberProfile()
    {
        var bookings = new FakeBookingRepository();
        var own = CreateBooking(Now.AddHours(2), BookingStatus.Confirmed);
        bookings.Add(own);
        bookings.Add(CreateBooking(Now.AddHours(2), BookingStatus.Confirmed, Guid.CreateVersion7()));

        var result = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(own.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task Upcoming_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var bookings = new FakeBookingRepository();
        var expected = new List<Booking>();

        for (var index = 0; index < 5; index++)
        {
            var booking = CreateBooking(Now.AddHours(index + 1), BookingStatus.Confirmed);
            expected.Add(booking);
            bookings.Add(booking);
        }

        var collected = await CollectAllAsync(bookings, "upcoming", pageSize: 2);

        Assert.Equal(
            expected.OrderBy(booking => booking.StartAt).Select(booking => booking.Id),
            collected.Select(item => item.Id));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Past_AcrossPages_HasNoDuplicatesAndNoGapsInDescendingOrder()
    {
        var bookings = new FakeBookingRepository();
        var expected = new List<Booking>();

        for (var index = 0; index < 5; index++)
        {
            var booking = CreateBooking(Now.AddDays(-index - 1), BookingStatus.Completed);
            expected.Add(booking);
            bookings.Add(booking);
        }

        var collected = await CollectAllAsync(bookings, "past", pageSize: 2);

        Assert.Equal(
            expected.OrderByDescending(booking => booking.StartAt).Select(booking => booking.Id),
            collected.Select(item => item.Id));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task NextCursor_IsNullOnIncompletePageAndSetOnFullPage()
    {
        var bookings = new FakeBookingRepository();
        bookings.Add(CreateBooking(Now.AddHours(1), BookingStatus.Confirmed));
        bookings.Add(CreateBooking(Now.AddHours(2), BookingStatus.Confirmed));

        var full = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery(null, null, 1), CancellationToken.None);
        var incomplete = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery(null, null, 2), CancellationToken.None);

        Assert.NotNull(full.Value.NextCursor);
        Assert.Null(incomplete.Value.NextCursor);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var result = await CreateHandler(new FakeBookingRepository())
            .Handle(new GetBarberBookingsQuery(null, "broken", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = CreateHandler(new FakeBookingRepository());

        var tooSmall = await handler.Handle(
            new GetBarberBookingsQuery(null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetBarberBookingsQuery(null, null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal("booking.getBarberBookings.pageSize", tooSmall.Error.Errors[0].Code);
        Assert.Equal("booking.getBarberBookings.pageSize", tooLarge.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithUnknownScope_ReturnsValidation()
    {
        var result = await CreateHandler(new FakeBookingRepository())
            .Handle(new GetBarberBookingsQuery("wat", null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.getBarberBookings.scope", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithoutBarberProfileClaim_ReturnsForbidden()
    {
        var handler = new GetBarberBookingsHandler(
            new FakeBookingRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            new FakeTimeProvider(Now));

        var result = await handler.Handle(
            new GetBarberBookingsQuery(null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Response_DoesNotExposeEmailOrIdentifiersOfRelatedAggregates()
    {
        var bookings = new FakeBookingRepository();
        bookings.Add(CreateBooking(Now.AddHours(1), BookingStatus.Confirmed));

        var result = await CreateHandler(bookings)
            .Handle(new GetBarberBookingsQuery(null, null, null), CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Value, JsonOptions);

        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("barberProfileId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotencyKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"customer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"displayName\"", json, StringComparison.Ordinal);
    }

    private static async Task<List<BarberBookingResponse>> CollectAllAsync(
        FakeBookingRepository bookings,
        string scope,
        int pageSize)
    {
        var handler = CreateHandler(bookings);
        var collected = new List<BarberBookingResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new GetBarberBookingsQuery(scope, cursor, pageSize),
                CancellationToken.None);

            Assert.True(page.IsSuccess);
            Assert.True(page.Value.Items.Count <= pageSize);
            collected.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        return collected;
    }

    private static GetBarberBookingsHandler CreateHandler(FakeBookingRepository bookings)
        => new(
            bookings,
            new FakeCurrentUser(Guid.CreateVersion7(), barberProfileId: BarberProfileId),
            new FakeTimeProvider(Now));

    private static Booking CreateBooking(
        DateTimeOffset startAt,
        BookingStatus status,
        Guid? barberProfileId = null)
    {
        var booking = Booking.Create(
            Guid.CreateVersion7(),
            barberProfileId ?? BarberProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(60),
            2400m,
            $"key-{Guid.NewGuid()}").Value;

        switch (status)
        {
            case BookingStatus.Confirmed:
                Assert.True(booking.Confirm().IsSuccess);
                break;
            case BookingStatus.Completed:
                Assert.True(booking.Confirm().IsSuccess);
                Assert.True(booking.Complete().IsSuccess);
                break;
            case BookingStatus.NoShow:
                Assert.True(booking.Confirm().IsSuccess);
                Assert.True(booking.MarkNoShow().IsSuccess);
                break;
            case BookingStatus.Cancelled:
                Assert.True(booking.Cancel().IsSuccess);
                break;
        }

        return booking;
    }
}
