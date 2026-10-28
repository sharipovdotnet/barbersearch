using BarberSearch.Application.Users.ChangeUserRole;
using BarberSearch.Application.Users.DeactivateUser;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Защита администраторов: нельзя менять роль и блокировать себя, нельзя оставить сервис
/// без активного администратора.
/// </summary>
public class AdminUserGuardTests
{
    [Fact]
    public async Task ChangeRole_ForSelf_ReturnsConflict()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        var handler = context.RoleChanger(admin.Id);

        var result = await handler.Handle(
            new ChangeUserRoleCommand(admin.Id, UserRole.Customer),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.role.self", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Users.UpdateCallCount);
        Assert.Equal(UserRole.Admin, admin.Role);
    }

    [Fact]
    public async Task ChangeRole_LastAdmin_ReturnsConflict()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        var handler = context.RoleChanger(Guid.CreateVersion7());

        var result = await handler.Handle(
            new ChangeUserRoleCommand(admin.Id, UserRole.Customer),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.admin.last", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Users.UpdateCallCount);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal(1, context.Users.AcquireAdminLockCallCount);
    }

    [Fact]
    public async Task ChangeRole_DemotesAdmin_WhenAnotherAdminExists()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        await context.AddAdminAsync();
        var handler = context.RoleChanger(Guid.CreateVersion7());

        var result = await handler.Handle(
            new ChangeUserRoleCommand(admin.Id, UserRole.Customer),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Customer, admin.Role);
        Assert.Equal(1, context.Users.UpdateCallCount);
    }

    [Fact]
    public async Task ChangeRole_CustomerToBarber_DoesNotCheckAdmins()
    {
        var context = new Context();
        var customer = User.Create(
            "customer@example.com",
            "hash:password",
            "Анна",
            "Петрова").Value;
        context.Users.Add(customer);
        var handler = context.RoleChanger(Guid.CreateVersion7());

        var result = await handler.Handle(
            new ChangeUserRoleCommand(customer.Id, UserRole.Barber),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Barber, customer.Role);
        // Блокировка берётся всегда: она сериализует операции с ролями, а не только понижение.
        Assert.Equal(1, context.Users.AcquireAdminLockCallCount);
    }

    [Fact]
    public async Task Deactivate_ForSelf_ReturnsConflict()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        var handler = context.Deactivator(admin.Id);

        var result = await handler.Handle(
            new DeactivateUserCommand(admin.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.deactivation.self", result.Error.Errors[0].Code);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task Deactivate_LastAdmin_ReturnsConflict()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        var handler = context.Deactivator(Guid.CreateVersion7());

        var result = await handler.Handle(
            new DeactivateUserCommand(admin.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.admin.last", result.Error.Errors[0].Code);
        Assert.True(admin.IsActive);
        Assert.Equal(0, context.Users.UpdateCallCount);
    }

    [Fact]
    public async Task Deactivate_Admin_WhenAnotherAdminExists_Succeeds()
    {
        var context = new Context();
        var admin = await context.AddAdminAsync();
        await context.AddAdminAsync();
        var handler = context.Deactivator(Guid.CreateVersion7());

        var result = await handler.Handle(
            new DeactivateUserCommand(admin.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(admin.IsActive);
    }

    [Fact]
    public async Task Deactivate_Customer_DoesNotCheckAdmins()
    {
        var context = new Context();
        var customer = User.Create(
            "customer@example.com",
            "hash:password",
            "Анна",
            "Петрова").Value;
        context.Users.Add(customer);
        var handler = context.Deactivator(Guid.CreateVersion7());

        var result = await handler.Handle(
            new DeactivateUserCommand(customer.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(customer.IsActive);
    }

    private sealed class Context
    {
        public FakeUserRepository Users { get; } = new();

        public FakeRefreshTokenRepository Tokens { get; } = new();

        public ChangeUserRoleHandler RoleChanger(Guid actorId) => new(
            Users,
            Tokens,
            new FakeUnitOfWork(Users, Tokens),
            new FakeCurrentUser(actorId, isAdmin: true),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ChangeUserRoleHandler>.Instance);

        public DeactivateUserHandler Deactivator(Guid actorId) => new(
            Users,
            Tokens,
            new FakeUnitOfWork(Users, Tokens),
            new FakeCurrentUser(actorId, isAdmin: true),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DeactivateUserHandler>.Instance);

        public Task<User> AddAdminAsync()
        {
            var admin = User.Create(
                $"admin-{Guid.NewGuid():N}@example.com",
                "hash:password",
                "Иван",
                "Админов").Value;

            admin.ChangeRole(UserRole.Admin);
            Users.Add(admin);

            return Task.FromResult(admin);
        }
    }
}
