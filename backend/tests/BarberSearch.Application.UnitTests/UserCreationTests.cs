using System.Net;
using BarberSearch.Application.Privacy;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class UserCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 14, 5, 0, TimeSpan.Zero);

    private static CreateUserHandler CreateHandler(
        FakeUserRepository repository,
        TimeProvider? timeProvider = null)
        => new(
            repository,
            timeProvider ?? new FakeTimeProvider(Now),
            NullLogger<CreateUserHandler>.Instance);

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

    [Fact]
    public async Task Create_WithoutConsentVersion_WhenConsentRequired_ReturnsConsentRequired()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand(
                "consent@example.com",
                "hash",
                "Ivan",
                "Petrov",
                RequireConsent: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.RegisterConsentRequired, result.Error.Errors[0].Code);
        Assert.Empty(repository.Users);
    }

    [Fact]
    public async Task Create_WithOutdatedConsentVersion_ReturnsConsentVersionMismatch()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand(
                "consent@example.com",
                "hash",
                "Ivan",
                "Petrov",
                ConsentVersion: "2026-01-01",
                RequireConsent: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.RegisterConsentVersion, result.Error.Errors[0].Code);
        Assert.Empty(repository.Users);
    }

    [Fact]
    public async Task Create_WithCurrentConsentVersion_StoresConsentFields()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);
        var ip = IPAddress.Parse("203.0.113.7");

        var result = await handler.Handle(
            new CreateUserCommand(
                "consent@example.com",
                "hash",
                "Ivan",
                "Petrov",
                ConsentVersion: $"  {PrivacyPolicy.Version}  ",
                ClientIp: ip,
                RequireConsent: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(repository.Users);
        Assert.Equal(PrivacyPolicy.Version, user.ConsentPolicyVersion);
        Assert.Equal(Now, user.ConsentedAt);
        Assert.Equal(ip, user.ConsentIp);
    }

    [Fact]
    public async Task Create_ForAdminPathWithoutConsentRequirement_StoresNoConsent()
    {
        var repository = new FakeUserRepository();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new CreateUserCommand("admin-created@example.com", "hash", "Ivan", "Petrov"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(repository.Users);
        Assert.Null(user.ConsentPolicyVersion);
        Assert.Null(user.ConsentedAt);
        Assert.Null(user.ConsentIp);
    }
}
