using BarberSearch.Application.Bookings.GetBookings;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Список броней: свои/чужие, admin, 404 для неизвестного клиента, пагинация.
/// </summary>
public class BookingReadTests
{
    [Fact]
    public async Task Get_NonAdmin_ReturnsOnlyOwnBookings()
    {
        var customerId = Guid.CreateVersion7();
        var bookings = new FakeBookingRepository();
        bookings.Add(CreateBooking(customerId, BaseTime));
        bookings.Add(CreateBooking(customerId, BaseTime.AddHours(1)));
        bookings.Add(CreateBooking(Guid.CreateVersion7(), BaseTime.AddHours(2)));

        var result = await CreateHandler(customerId, bookings)
            .Handle(new GetBookingsQuery(null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Equal(customerId, item.UserId));
    }

    [Fact]
    public async Task Get_NonAdmin_WithForeignUserId_ReturnsForbidden()
    {
        var customerId = Guid.CreateVersion7();
        var handler = CreateHandler(customerId, new FakeBookingRepository());

        var result = await handler.Handle(
            new GetBookingsQuery(Guid.CreateVersion7(), null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_AdminWithoutFilter_ReturnsAllBookings()
    {
        var bookings = new FakeBookingRepository();
        bookings.Add(CreateBooking(Guid.CreateVersion7(), BaseTime));
        bookings.Add(CreateBooking(Guid.CreateVersion7(), BaseTime.AddHours(1)));

        var result = await CreateHandler(
                Guid.CreateVersion7(),
                bookings,
                isAdmin: true)
            .Handle(new GetBookingsQuery(null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task Get_AdminWithUserId_ReturnsOnlyThatClient()
    {
        var targetUserId = Guid.CreateVersion7();
        var bookings = new FakeBookingRepository();
        bookings.Add(CreateBooking(targetUserId, BaseTime));
        bookings.Add(CreateBooking(Guid.CreateVersion7(), BaseTime.AddHours(1)));

        var users = new FakeUserRepository();
        users.AddExisting(targetUserId);

        var result = await CreateHandler(
                Guid.CreateVersion7(),
                bookings,
                isAdmin: true,
                users)
            .Handle(new GetBookingsQuery(targetUserId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(targetUserId, result.Value.Items[0].UserId);
    }

    [Fact]
    public async Task Get_AdminWithUnknownUserId_ReturnsNotFound()
    {
        var handler = CreateHandler(
            Guid.CreateVersion7(),
            new FakeBookingRepository(),
            isAdmin: true);

        var result = await handler.Handle(
            new GetBookingsQuery(Guid.CreateVersion7(), null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var customerId = Guid.CreateVersion7();
        var bookings = new FakeBookingRepository();
        var expected = new List<Booking>();

        for (var index = 0; index < 5; index++)
        {
            var booking = CreateBooking(customerId, BaseTime.AddHours(index));
            expected.Add(booking);
            bookings.Add(booking);
        }

        var handler = CreateHandler(customerId, bookings);
        var collected = new List<BookingResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new GetBookingsQuery(null, null, cursor, 2),
                CancellationToken.None);

            Assert.True(page.IsSuccess);
            Assert.True(page.Value.Items.Count <= 2);
            collected.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        var expectedOrder = expected
            .OrderByDescending(booking => booking.StartAt)
            .ThenBy(booking => booking.Id)
            .Select(booking => booking.Id);

        Assert.Equal(expectedOrder, collected.Select(item => item.Id));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var result = await CreateHandler(Guid.CreateVersion7(), new FakeBookingRepository())
            .Handle(new GetBookingsQuery(null, null, "broken", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = CreateHandler(Guid.CreateVersion7(), new FakeBookingRepository());

        var tooSmall = await handler.Handle(
            new GetBookingsQuery(null, null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetBookingsQuery(null, null, null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge.Error.Errors[0].Type);
    }

    /// <summary>
    /// Неизвестный скоуп — ошибка параметра, а не пустой список: клиент различает
    /// опечатку в запросе и «броней нет».
    /// </summary>
    [Fact]
    public async Task Get_WithUnknownScope_ReturnsValidation()
    {
        var result = await CreateHandler(Guid.CreateVersion7(), new FakeBookingRepository())
            .Handle(new GetBookingsQuery(null, "soon", null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Booking.GetBookingsScope,
            result.Error.Errors[0].Code);
    }

    private static readonly DateTimeOffset BaseTime =
        new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    private static GetBookingsHandler CreateHandler(
        Guid currentUserId,
        FakeBookingRepository bookings,
        bool isAdmin = false,
        FakeUserRepository? users = null)
        => new(
            bookings,
            users ?? new FakeUserRepository(),
            new FakeCurrentUser(currentUserId, isAdmin),
            new FakeTimeProvider(BaseTime));

    private static Booking CreateBooking(Guid userId, DateTimeOffset startAt)
        => Booking.Create(
            userId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            30m,
            $"key-{Guid.NewGuid()}").Value;
}
