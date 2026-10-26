using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class UserCreationTests
{
    private static CreateUserHandler CreateHandler(FakeUserRepository repository)
        => new(repository, NullLogger<CreateUserHandler>.Instance);

    [Fact]
    public async Task Create_TrimsAndLowercasesEmailBeforeSaving()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand("  User@Example.COM  ", "hash", "Ivan", "Petrov"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(repository.Users);
        Assert.Equal("user@example.com", user.Email);
    }

    [Fact]
    public async Task Create_WhenNormalizedEmailAlreadyExists_ReturnsConflict()
    {
        var existing = User.Create("user@example.com", "hash", "Existing", "User").Value;
        var repository = new FakeUserRepository();
        repository.Add(existing);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand(" User@Example.COM ", "hash", "Ivan", "Petrov"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.create.emailTaken", result.Error.Errors[0].Code);
        Assert.Single(repository.Users);
    }

    [Fact]
    public async Task Create_WithInvalidEmail_DoesNotAddUser()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand("a@b", "hash", "Ivan", "Petrov"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("user.email", result.Error.Errors[0].Code);
        Assert.Empty(repository.Users);
    }
}
