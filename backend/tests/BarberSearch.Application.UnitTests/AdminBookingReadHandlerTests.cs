using BarberSearch.Application.Bookings.Admin;
using BarberSearch.Application.Bookings.Admin.GetAdminBooking;
using BarberSearch.Application.Bookings.Admin.GetAdminBookings;
using BarberSearch.Application.Common;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Обработчики админского чтения броней: разбор статуса и поиска, размер страницы, курсор,
/// порядок строк, проброс ошибки репозитория и карточка по идентификатору.
/// </summary>
public class AdminBookingReadHandlerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsNewestFirstAcrossPages()
    {
        var repository = new FakeBookingRepository();
        var oldest = CreateRow(Start);
        var middle = CreateRow(Start.AddMinutes(10));
        var newest = CreateRow(Start.AddMinutes(20));
        repository.AddAdminRow(oldest);
        repository.AddAdminRow(middle);
        repository.AddAdminRow(newest);

        var handler = new GetAdminBookingsHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminBookingsQuery(null, null, null, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(
            new[] { newest.Id, middle.Id },
            firstPage.Value.Items.Select(row => row.Id));
        Assert.NotNull(firstPage.Value.NextCursor);
        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница.
        Assert.Equal(3, repository.LastAdminLimit);

        var secondPage = await handler.Handle(
            new GetAdminBookingsQuery(null, null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(
            new[] { oldest.Id },
            secondPage.Value.Items.Select(row => row.Id));
        Assert.Null(secondPage.Value.NextCursor);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("all", null)]
    [InlineData("Pending", BookingStatus.Pending)]
    [InlineData("confirmed", BookingStatus.Confirmed)]
    [InlineData(" NoShow ", BookingStatus.NoShow)]
    public async Task Get_ParsesStatusNamesCaseInsensitively(
        string? value,
        BookingStatus? expected)
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(value, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, repository.LastAdminFilters!.Status);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("upcoming")]
    [InlineData("0")]
    [InlineData("3")]
    public async Task Get_WithUnknownStatus_ReturnsValidation(string value)
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(value, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.AdminBooking.StatusInvalid, result.Error.Errors[0].Code);
        Assert.Null(repository.LastAdminFilters);
    }

    [Fact]
    public async Task Get_TrimsSearchAndPassesItToRepository()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(null, "  e2e-bookings@barbersearch.test  ", null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "e2e-bookings@barbersearch.test",
            repository.LastAdminFilters!.Search);
    }

    [Fact]
    public async Task Get_WithBlankSearch_DoesNotFilter()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        await handler.Handle(
            new GetAdminBookingsQuery(null, "   ", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastAdminFilters!.Search);
    }

    [Fact]
    public async Task Get_WithTooLongSearch_ReturnsValidation()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(null, new string('a', 101), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.AdminBooking.SearchTooLong, result.Error.Errors[0].Code);
        Assert.Null(repository.LastAdminFilters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Get_WithPageSizeOutOfRange_ReturnsValidation(int pageSize)
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(null, null, null, pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            $"{ApiErrorCodes.AdminBooking.PageSizePrefix}.pageSize",
            result.Error.Errors[0].Code);
        Assert.Null(repository.LastAdminFilters);
    }

    [Fact]
    public async Task Get_WithoutPageSize_UsesDefault()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        await handler.Handle(
            new GetAdminBookingsQuery(null, null, null, null),
            CancellationToken.None);

        Assert.Equal(Pagination.DEFAULT_PAGE_SIZE + 1, repository.LastAdminLimit);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(null, null, "garbage", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.bookings.cursor.invalid", result.Error.Errors[0].Code);
        Assert.Null(repository.LastAdminFilters);
    }

    [Fact]
    public async Task Get_PassesDecodedCursorToRepository()
    {
        var repository = new FakeBookingRepository();
        var handler = new GetAdminBookingsHandler(repository);
        var cursor = new AdminBookingCursor(Start, Guid.CreateVersion7());

        await handler.Handle(
            new GetAdminBookingsQuery(null, null, cursor.Encode(), null),
            CancellationToken.None);

        Assert.Equal(cursor, repository.LastAdminCursor);
    }

    [Fact]
    public async Task Get_PropagatesRepositoryFailure()
    {
        var repository = new FakeBookingRepository
        {
            ForcedAdminFailure = new Failure(Error.Validation(
                "admin.bookings.page.limit",
                "The page limit is not positive.",
                "limit"))
        };
        var handler = new GetAdminBookingsHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingsQuery(null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.bookings.page.limit", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_ReturnsBookingFromRepository()
    {
        var repository = new FakeBookingRepository();
        var expected = CreateRow(Start);
        repository.AddAdminRow(expected);
        var handler = new GetAdminBookingHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingQuery(expected.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task GetById_WhenMissing_ReturnsNotFound()
    {
        var handler = new GetAdminBookingHandler(new FakeBookingRepository());

        var result = await handler.Handle(
            new GetAdminBookingQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.AdminBooking.NotFound, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_PropagatesRepositoryFailure()
    {
        var repository = new FakeBookingRepository
        {
            ForcedAdminFailure = new Failure(Error.NotFound(
                "admin.bookings.notFound",
                "The booking was not found."))
        };
        var handler = new GetAdminBookingHandler(repository);

        var result = await handler.Handle(
            new GetAdminBookingQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.AdminBooking.NotFound, result.Error.Errors[0].Code);
    }

    private static AdminBookingResponse CreateRow(
        DateTimeOffset startAt,
        BookingStatus status = BookingStatus.Pending,
        string email = "e2e-bookings@barbersearch.test")
        => new(
            Guid.CreateVersion7(),
            status,
            startAt,
            startAt.AddMinutes(30),
            45m,
            "Позвоните за час",
            null,
            null,
            startAt.AddDays(-1),
            new AdminBookingCustomerResponse(
                Guid.CreateVersion7(),
                "Иван",
                "Петров",
                email,
                "+7 999 123-45-67",
                AvatarUrl: null),
            new AdminBookingBarberResponse(
                Guid.CreateVersion7(),
                "Барбер Тестов",
                AvatarUrl: null),
            new AdminBookingServiceResponse("Стрижка"),
            new AdminBookingSalonResponse(
                Guid.CreateVersion7(),
                "Салон",
                "ул. Тестовая, 1",
                "Москва",
                "Europe/Moscow"));
}
