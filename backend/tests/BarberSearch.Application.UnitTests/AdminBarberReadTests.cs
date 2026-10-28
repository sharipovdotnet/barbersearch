using BarberSearch.Application.BarberProfiles.Admin;
using BarberSearch.Application.BarberProfiles.Admin.GetAdminBarber;
using BarberSearch.Application.BarberProfiles.Admin.GetAdminBarbers;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Domain.BarberProfiles.Enums;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Админское чтение барберов: фильтр по статусу, порядок страниц, поиск, курсор и карточка.
/// </summary>
public class AdminBarberReadTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_Pending_ReturnsOldestFirstAcrossPages()
    {
        var repository = new FakeBarberProfileRepository();
        var oldest = CreateBarber(verified: false, active: true, Start, "Oldest");
        var middle = CreateBarber(verified: false, active: true, Start.AddMinutes(10), "Middle");
        var newest = CreateBarber(verified: false, active: true, Start.AddMinutes(20), "Newest");
        repository.AddAdminBarber(oldest);
        repository.AddAdminBarber(middle);
        repository.AddAdminBarber(newest);
        repository.AddAdminBarber(CreateBarber(verified: true, active: true, Start.AddMinutes(30), "Verified"));

        var handler = new GetAdminBarbersHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminBarbersQuery("pending", null, null, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(
            new[] { oldest.Id, middle.Id },
            firstPage.Value.Items.Select(barber => barber.Id));
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await handler.Handle(
            new GetAdminBarbersQuery("pending", null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(new[] { newest.Id }, secondPage.Value.Items.Select(barber => barber.Id));
        Assert.Null(secondPage.Value.NextCursor);
    }

    [Fact]
    public async Task Get_Hidden_ReturnsOnlyInactiveNewestFirst()
    {
        var repository = new FakeBarberProfileRepository();
        var older = CreateBarber(verified: true, active: false, Start, "Older");
        var newer = CreateBarber(verified: false, active: false, Start.AddHours(1), "Newer");
        repository.AddAdminBarber(older);
        repository.AddAdminBarber(newer);
        repository.AddAdminBarber(CreateBarber(verified: true, active: true, Start.AddHours(2), "Visible"));

        var handler = new GetAdminBarbersHandler(repository);

        var result = await handler.Handle(
            new GetAdminBarbersQuery("hidden", null, null, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { newer.Id, older.Id },
            result.Value.Items.Select(barber => barber.Id));
        Assert.All(result.Value.Items, barber => Assert.False(barber.IsActive));
    }

    [Fact]
    public async Task Get_TrimsSearchAndPassesItToRepository()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new GetAdminBarbersHandler(repository);

        var result = await handler.Handle(
            new GetAdminBarbersQuery(null, "  Артём  ", null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Артём", repository.LastAdminSearch);
        Assert.Equal(AdminBarberStatus.All, repository.LastAdminStatus);
        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница.
        Assert.Equal(21, repository.LastAdminLimit);
    }

    [Fact]
    public async Task Get_WithBlankSearch_DoesNotFilter()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new GetAdminBarbersHandler(repository);

        await handler.Handle(
            new GetAdminBarbersQuery(null, "   ", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastAdminSearch);
    }

    [Fact]
    public async Task Get_WithUnknownStatus_ReturnsValidation()
    {
        var handler = new GetAdminBarbersHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminBarbersQuery("rejected", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.barbers.status", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetAdminBarbersHandler(new FakeBarberProfileRepository());

        var tooSmall = await handler.Handle(
            new GetAdminBarbersQuery(null, null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetAdminBarbersQuery(null, null, null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal("admin.barbers.pageSize", tooSmall.Error.Errors[0].Code);
        Assert.Equal("admin.barbers.pageSize", tooLarge.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithTooLongSearch_ReturnsValidation()
    {
        var handler = new GetAdminBarbersHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminBarbersQuery(null, new string('a', 101), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.barbers.search", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var handler = new GetAdminBarbersHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminBarbersQuery(null, null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.barbers.cursor.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_ReturnsReadinessChecklist()
    {
        var repository = new FakeBarberProfileRepository();
        var barber = CreateDetail();

        repository.AddAdminBarberDetail(barber);

        var handler = new GetAdminBarberHandler(repository);

        var result = await handler.Handle(
            new GetAdminBarberQuery(barber.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(barber.Id, result.Value.Id);
        Assert.Equal("artem@example.com", result.Value.Email);
        Assert.Equal(Grade.SeniorBarber, result.Value.Grade);
        Assert.True(result.Value.Readiness.IsProfileFilled);
        Assert.True(result.Value.Readiness.IsEmailVerified);
        Assert.Equal(2, result.Value.Readiness.ServicesCount);
        Assert.Equal(1, result.Value.Readiness.SalonsCount);
        Assert.Equal(5, result.Value.Readiness.ScheduleDaysCount);
        Assert.Equal(3, result.Value.Readiness.PhotosCount);
    }

    [Fact]
    public async Task GetById_WhenMissing_ReturnsNotFound()
    {
        var handler = new GetAdminBarberHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminBarberQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_WithEmptyId_ReturnsValidation()
    {
        var handler = new GetAdminBarberHandler(new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetAdminBarberQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.barbers.id", result.Error.Errors[0].Code);
    }

    private static AdminBarberResponse CreateBarber(
        bool verified,
        bool active,
        DateTimeOffset createdAt,
        string displayName)
        => new(
            Guid.CreateVersion7(),
            displayName,
            $"{displayName.ToLowerInvariant()}@example.com",
            Grade.None,
            null,
            0,
            active,
            verified,
            createdAt,
            createdAt);

    private static AdminBarberDetailResponse CreateDetail()
        => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Артём Соколов",
            "artem@example.com",
            "+7 999 000-00-00",
            null,
            Grade.SeniorBarber,
            "Делаю фейды и классику.",
            8,
            4.8m,
            24,
            true,
            true,
            new AdminBarberReadinessResponse(
                IsProfileFilled: true,
                IsEmailVerified: true,
                ServicesCount: 2,
                SalonsCount: 1,
                ScheduleDaysCount: 5,
                PhotosCount: 3),
            Start,
            Start);
}
