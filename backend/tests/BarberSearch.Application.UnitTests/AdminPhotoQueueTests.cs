using BarberSearch.Application.Photos.Admin;
using BarberSearch.Application.Photos.Admin.GetAdminAvatarPhotos;
using BarberSearch.Application.Photos.Admin.GetAdminPortfolioPhotos;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Admin;
using BarberSearch.Domain.Common;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чтения админских очередей фотографий: проверка роли, разбор параметров, FIFO-порядок,
/// лимит <c>pageSize + 1</c>, курсор следующей страницы и коды ошибок.
/// </summary>
/// <remarks>
/// Предикат «только <c>Pending</c>» и SQL-порядок проверяют инфраструктурные тесты
/// <c>ToQueryString()</c>: фейк хранит уже готовые строки, а обработчик проверяется
/// на решения о параметрах и построении курсора.
/// </remarks>
public class AdminPhotoQueueTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAvatars_ByAdmin_ReturnsFifoPageAndAsksForPageSizePlusOne()
    {
        var repository = new FakeUserRepository();
        var oldest = CreateAvatarRow(Start, "Oldest");
        var middle = CreateAvatarRow(Start.AddMinutes(10), "Middle");
        var newest = CreateAvatarRow(Start.AddMinutes(20), "Newest");
        repository.AddAdminAvatarRow(newest);
        repository.AddAdminAvatarRow(oldest);
        repository.AddAdminAvatarRow(middle);

        var handler = CreateAvatarHandler(repository);

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, null, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { oldest.UserId, middle.UserId },
            result.Value.Items.Select(row => row.UserId));
        Assert.NotNull(result.Value.NextCursor);
        Assert.Equal(3, repository.LastAvatarPhotoLimit);
    }

    [Fact]
    public async Task GetAvatars_SecondPage_UsesCursorAndReturnsTheRest()
    {
        var repository = new FakeUserRepository();
        var first = CreateAvatarRow(Start, "First");
        var second = CreateAvatarRow(Start.AddMinutes(10), "Second");
        var third = CreateAvatarRow(Start.AddMinutes(20), "Third");
        repository.AddAdminAvatarRow(first);
        repository.AddAdminAvatarRow(second);
        repository.AddAdminAvatarRow(third);

        var handler = CreateAvatarHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, null, 2),
            CancellationToken.None);

        var secondPage = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(new[] { third.UserId }, secondPage.Value.Items.Select(row => row.UserId));
        Assert.Null(secondPage.Value.NextCursor);

        var cursor = Assert.IsType<AdminPhotoCursor>(repository.LastAvatarPhotoCursor);
        Assert.Equal(second.SubmittedAt, cursor.SubmittedAt);
        Assert.Equal(second.UserId, cursor.Id);
    }

    [Fact]
    public async Task GetAvatars_TrimsSearchAndBlankSearchDoesNotFilter()
    {
        var repository = new FakeUserRepository();
        var handler = CreateAvatarHandler(repository);

        await handler.Handle(
            new GetAdminAvatarPhotosQuery("  Иван  ", null, null),
            CancellationToken.None);

        Assert.Equal("Иван", repository.LastAvatarPhotoSearch);

        await handler.Handle(
            new GetAdminAvatarPhotosQuery("   ", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastAvatarPhotoSearch);
        Assert.Equal(21, repository.LastAvatarPhotoLimit);
    }

    [Fact]
    public async Task GetAvatars_WithTooLongSearch_ReturnsValidation()
    {
        var handler = CreateAvatarHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(new string('a', 101), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.search", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetAvatars_WithPageSizeOutsideBounds_ReturnsValidation(int pageSize)
    {
        var handler = CreateAvatarHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, null, pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.pageSize", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetAvatars_WithBrokenCursor_ReturnsValidation()
    {
        var handler = CreateAvatarHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.cursor", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetAvatars_ByNonAdmin_ReturnsForbiddenWithoutReadingTheQueue()
    {
        var repository = new FakeUserRepository();
        repository.AddAdminAvatarRow(CreateAvatarRow(Start, "Hidden"));

        var handler = new GetAdminAvatarPhotosHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Null(repository.LastAvatarPhotoLimit);
    }

    [Fact]
    public async Task GetAvatars_Anonymous_ReturnsUnauthorized()
    {
        var handler = new GetAdminAvatarPhotosHandler(
            new FakeUserRepository(),
            FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new GetAdminAvatarPhotosQuery(null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetPortfolio_ByAdmin_ReturnsFifoPageAndAsksForPageSizePlusOne()
    {
        var repository = new FakeBarberProfileRepository();
        var oldest = CreatePortfolioRow(Start, "Oldest");
        var middle = CreatePortfolioRow(Start.AddMinutes(10), "Middle");
        var newest = CreatePortfolioRow(Start.AddMinutes(20), "Newest");
        repository.AddAdminPortfolioPhotoRow(newest);
        repository.AddAdminPortfolioPhotoRow(oldest);
        repository.AddAdminPortfolioPhotoRow(middle);

        var handler = CreatePortfolioHandler(repository);

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, null, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { oldest.PhotoId, middle.PhotoId },
            result.Value.Items.Select(row => row.PhotoId));
        Assert.NotNull(result.Value.NextCursor);
        Assert.Equal(3, repository.LastPortfolioPhotoLimit);
    }

    [Fact]
    public async Task GetPortfolio_SecondPage_UsesCursorAndReturnsTheRest()
    {
        var repository = new FakeBarberProfileRepository();
        var first = CreatePortfolioRow(Start, "First");
        var second = CreatePortfolioRow(Start.AddMinutes(10), "Second");
        var third = CreatePortfolioRow(Start.AddMinutes(20), "Third");
        repository.AddAdminPortfolioPhotoRow(first);
        repository.AddAdminPortfolioPhotoRow(second);
        repository.AddAdminPortfolioPhotoRow(third);

        var handler = CreatePortfolioHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, null, 2),
            CancellationToken.None);

        var secondPage = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(
            new[] { third.PhotoId },
            secondPage.Value.Items.Select(row => row.PhotoId));
        Assert.Null(secondPage.Value.NextCursor);

        var cursor = Assert.IsType<AdminPhotoCursor>(repository.LastPortfolioPhotoCursor);
        Assert.Equal(second.SubmittedAt, cursor.SubmittedAt);
        Assert.Equal(second.PhotoId, cursor.Id);
    }

    [Fact]
    public async Task GetPortfolio_TrimsSearchAndBlankSearchDoesNotFilter()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = CreatePortfolioHandler(repository);

        await handler.Handle(
            new GetAdminPortfolioPhotosQuery("  Артём  ", null, null),
            CancellationToken.None);

        Assert.Equal("Артём", repository.LastPortfolioPhotoSearch);

        await handler.Handle(
            new GetAdminPortfolioPhotosQuery("   ", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastPortfolioPhotoSearch);
        Assert.Equal(21, repository.LastPortfolioPhotoLimit);
    }

    [Fact]
    public async Task GetPortfolio_WithTooLongSearch_ReturnsValidation()
    {
        var handler = CreatePortfolioHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(new string('a', 101), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.search", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetPortfolio_WithPageSizeOutsideBounds_ReturnsValidation(int pageSize)
    {
        var handler = CreatePortfolioHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, null, pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.pageSize", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetPortfolio_WithBrokenCursor_ReturnsValidation()
    {
        var handler = CreatePortfolioHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.photos.cursor", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetPortfolio_ByNonAdmin_ReturnsForbiddenWithoutReadingTheQueue()
    {
        var repository = new FakeBarberProfileRepository();
        repository.AddAdminPortfolioPhotoRow(CreatePortfolioRow(Start, "Hidden"));

        var handler = new GetAdminPortfolioPhotosHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Null(repository.LastPortfolioPhotoLimit);
    }

    [Fact]
    public async Task GetPortfolio_Anonymous_ReturnsUnauthorized()
    {
        var handler = new GetAdminPortfolioPhotosHandler(
            new FakeBarberProfileRepository(),
            FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new GetAdminPortfolioPhotosQuery(null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    private static GetAdminAvatarPhotosHandler CreateAvatarHandler(
        FakeUserRepository repository)
        => new(repository, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

    private static GetAdminPortfolioPhotosHandler CreatePortfolioHandler(
        FakeBarberProfileRepository repository)
        => new(repository, new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

    private static AdminAvatarPhotoResponse CreateAvatarRow(
        DateTimeOffset submittedAt,
        string name)
        => new(
            Guid.CreateVersion7(),
            name,
            "Тестов",
            $"{name.ToLowerInvariant()}@example.com",
            "https://cdn.example.com/avatar.png",
            PhotoModerationStatus.Pending,
            submittedAt);

    private static AdminPortfolioPhotoResponse CreatePortfolioRow(
        DateTimeOffset submittedAt,
        string barberName)
        => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            barberName,
            $"{barberName.ToLowerInvariant()}@example.com",
            "https://cdn.example.com/photo.png",
            "Подпись",
            IsPublished: true,
            PhotoModerationStatus.Pending,
            submittedAt);
}
