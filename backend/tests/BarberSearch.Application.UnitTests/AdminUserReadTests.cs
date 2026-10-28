using BarberSearch.Application.Users.Admin;
using BarberSearch.Application.Users.Admin.GetAdminUser;
using BarberSearch.Application.Users.Admin.GetAdminUsers;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Domain.Users;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Админское чтение пользователей: фильтры статуса и роли, порядок страниц, поиск,
/// курсор и карточка с профилем барбера.
/// </summary>
public class AdminUserReadTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsNewestFirstAcrossPages()
    {
        var repository = new FakeUserRepository();
        var oldest = CreateRow(Start, "Oldest");
        var middle = CreateRow(Start.AddMinutes(10), "Middle");
        var newest = CreateRow(Start.AddMinutes(20), "Newest");
        repository.AddAdminRow(oldest);
        repository.AddAdminRow(middle);
        repository.AddAdminRow(newest);

        var handler = new GetAdminUsersHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminUsersQuery(null, null, null, null, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(
            new[] { newest.Id, middle.Id },
            firstPage.Value.Items.Select(user => user.Id));
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await handler.Handle(
            new GetAdminUsersQuery(null, null, null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(
            new[] { oldest.Id },
            secondPage.Value.Items.Select(user => user.Id));
        Assert.Null(secondPage.Value.NextCursor);
    }

    [Fact]
    public async Task Get_ActiveFilter_PassesStatusAndHidesBlocked()
    {
        var repository = new FakeUserRepository();
        var active = CreateRow(Start, "Active");
        var blocked = CreateRow(Start.AddMinutes(1), "Blocked") with { IsActive = false };
        repository.AddAdminRow(active);
        repository.AddAdminRow(blocked);

        var handler = new GetAdminUsersHandler(repository);

        var result = await handler.Handle(
            new GetAdminUsersQuery("active", null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminUserStatus.Active, repository.LastAdminStatus);
        Assert.Equal(new[] { active.Id }, result.Value.Items.Select(user => user.Id));
    }

    [Fact]
    public async Task Get_UnverifiedFilter_ReturnsUsersWithoutVerifiedEmail()
    {
        var repository = new FakeUserRepository();
        var unverified = CreateRow(Start, "Unverified") with { IsEmailVerified = false };
        repository.AddAdminRow(unverified);
        repository.AddAdminRow(CreateRow(Start.AddMinutes(1), "Verified"));

        var handler = new GetAdminUsersHandler(repository);

        var result = await handler.Handle(
            new GetAdminUsersQuery("unverified", null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { unverified.Id },
            result.Value.Items.Select(user => user.Id));
    }

    [Fact]
    public async Task Get_RoleFilter_ParsesNameCaseInsensitively()
    {
        var repository = new FakeUserRepository();
        repository.AddAdminRow(CreateRow(Start, "Admin", UserRole.Admin));
        repository.AddAdminRow(CreateRow(Start.AddMinutes(1), "Customer"));

        var handler = new GetAdminUsersHandler(repository);

        var result = await handler.Handle(
            new GetAdminUsersQuery(null, "admin", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Admin, repository.LastAdminRole);
        Assert.Single(result.Value.Items);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("3")]
    public async Task Get_WithUnknownRole_ReturnsValidation(string role)
    {
        var handler = new GetAdminUsersHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminUsersQuery(null, role, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.users.role", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_TrimsSearchAndPassesItToRepository()
    {
        var repository = new FakeUserRepository();
        var handler = new GetAdminUsersHandler(repository);

        var result = await handler.Handle(
            new GetAdminUsersQuery(null, null, "  Артём  ", null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Артём", repository.LastAdminSearch);
        Assert.Equal(AdminUserStatus.All, repository.LastAdminStatus);
        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница.
        Assert.Equal(21, repository.LastAdminLimit);
    }

    [Fact]
    public async Task Get_WithBlankSearch_DoesNotFilter()
    {
        var repository = new FakeUserRepository();
        var handler = new GetAdminUsersHandler(repository);

        await handler.Handle(
            new GetAdminUsersQuery(null, null, "   ", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastAdminSearch);
    }

    [Fact]
    public async Task Get_WithUnknownStatus_ReturnsValidation()
    {
        var handler = new GetAdminUsersHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminUsersQuery("rejected", null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.users.status", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetAdminUsersHandler(new FakeUserRepository());

        var tooSmall = await handler.Handle(
            new GetAdminUsersQuery(null, null, null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new GetAdminUsersQuery(null, null, null, null, 101),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal("admin.users.pageSize", tooSmall.Error.Errors[0].Code);
        Assert.Equal("admin.users.pageSize", tooLarge.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var handler = new GetAdminUsersHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminUsersQuery(null, null, null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.users.cursor.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_ReturnsBarberProfileLink()
    {
        var repository = new FakeUserRepository();
        var user = User.Create(
            "barber@example.com",
            "hash:password",
            "Артём",
            "Соколов").Value;
        var profileId = Guid.CreateVersion7();

        user.LinkBarberProfile(profileId);
        repository.Add(user);
        repository.AddBarberProfile(profileId, "Артём Соколов", isVerified: true);

        var handler = new GetAdminUserHandler(repository);

        var result = await handler.Handle(
            new GetAdminUserQuery(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("barber@example.com", result.Value.Email);
        Assert.Equal(profileId, result.Value.BarberProfileId);
        Assert.NotNull(result.Value.BarberProfile);
        Assert.Equal("Артём Соколов", result.Value.BarberProfile.DisplayName);
        Assert.True(result.Value.BarberProfile.IsVerified);
    }

    [Fact]
    public async Task GetById_ForCustomer_ReturnsNoBarberProfile()
    {
        var repository = new FakeUserRepository();
        var user = User.Create(
            "customer@example.com",
            "hash:password",
            "Анна",
            "Петрова").Value;
        repository.Add(user);

        var handler = new GetAdminUserHandler(repository);

        var result = await handler.Handle(
            new GetAdminUserQuery(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.BarberProfileId);
        Assert.Null(result.Value.BarberProfile);
    }

    [Fact]
    public async Task GetById_WhenMissing_ReturnsNotFound()
    {
        var handler = new GetAdminUserHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminUserQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("admin.users.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetById_WithEmptyId_ReturnsValidation()
    {
        var handler = new GetAdminUserHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new GetAdminUserQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("admin.users.id", result.Error.Errors[0].Code);
    }

    private static AdminUserResponse CreateRow(
        DateTimeOffset createdAt,
        string name,
        UserRole role = UserRole.Customer,
        bool isActive = true,
        bool isEmailVerified = true)
        => new(
            Guid.CreateVersion7(),
            $"{name.ToLowerInvariant()}@example.com",
            name,
            "Тестов",
            role,
            isActive,
            isEmailVerified,
            false,
            null,
            null,
            createdAt);
}
