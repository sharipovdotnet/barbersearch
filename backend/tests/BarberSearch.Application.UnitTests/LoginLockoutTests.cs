using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Domain;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Блокировка входа на уровне аккаунта (R-21): счётчик неудач, задержка и её сброс.
/// </summary>
/// <remarks>
/// Часы управляемые (<see cref="FakeTimeProvider"/>), поэтому «сейчас» в обработчике и в
/// утверждениях — одно и то же значение, без ожидания реального времени.
/// </remarks>
public class LoginLockoutTests
{
    private const string CorrectPassword = "correct-password";

    private static readonly DateTimeOffset LoginStart =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_WithInvalidPassword_IncrementsCounterAndSaves()
    {
        var context = CreateContext();
        context.Clock.UtcNow = LoginStart;

        var result = await context.Handler.Handle(
            new LoginUserCommand(context.User.Email, "wrong-password"),
            CancellationToken.None);

        AssertFailure(result, "auth.login.invalidCredentials");
        Assert.Equal(1, context.User.FailedLoginAttempts);
        Assert.Equal(LoginStart, context.User.LastFailedLoginAt);
        Assert.Null(context.User.LockoutEnd);
        Assert.Equal(1, context.Repository.UpdateCallCount);
    }

    [Fact]
    public async Task Login_WithFiveInvalidPasswords_LocksTheAccount()
    {
        var context = CreateContext();

        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            context.Clock.UtcNow = LoginStart.AddMinutes(attempt);

            var result = await context.Handler.Handle(
                new LoginUserCommand(context.User.Email, "wrong-password"),
                CancellationToken.None);

            AssertFailure(result, "auth.login.invalidCredentials");
        }

        Assert.Equal(
            Constants.MAX_FAILED_LOGIN_ATTEMPTS,
            context.User.FailedLoginAttempts);
        Assert.True(context.User.IsLockedOut(context.Clock.UtcNow));
        Assert.Equal(
            context.Clock.UtcNow.AddMinutes(Constants.LOGIN_LOCKOUT_BASE_MINUTES),
            context.User.LockoutEnd);
        Assert.Equal(Constants.MAX_FAILED_LOGIN_ATTEMPTS, context.Repository.UpdateCallCount);
    }

    [Fact]
    public async Task Login_WithCorrectPasswordWhileLockedOut_ReturnsLockedOutWithoutSaving()
    {
        var context = CreateContext();
        await LockAccountAsync(context);

        var updateCallsBefore = context.Repository.UpdateCallCount;
        var stampBefore = context.User.SecurityStamp;
        var lockoutEndBefore = context.User.LockoutEnd;

        var result = await context.Handler.Handle(
            new LoginUserCommand(context.User.Email, CorrectPassword),
            CancellationToken.None);

        AssertFailure(result, "auth.login.lockedOut");
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);

        // Вход не записан: ни LastLoginAt, ни сохранения, ни нового security stamp.
        Assert.Null(context.User.LastLoginAt);
        Assert.Equal(updateCallsBefore, context.Repository.UpdateCallCount);
        Assert.Equal(stampBefore, context.User.SecurityStamp);
        Assert.Equal(lockoutEndBefore, context.User.LockoutEnd);
    }

    [Fact]
    public async Task Login_WithInvalidPasswordWhileLockedOut_DoesNotExtendLockout()
    {
        var context = CreateContext();
        await LockAccountAsync(context);

        var updateCallsBefore = context.Repository.UpdateCallCount;
        var attemptsBefore = context.User.FailedLoginAttempts;
        var lockoutEndBefore = context.User.LockoutEnd;

        var result = await context.Handler.Handle(
            new LoginUserCommand(context.User.Email, "wrong-password"),
            CancellationToken.None);

        AssertFailure(result, "auth.login.invalidCredentials");

        // Счётчик не растёт и блокировка не продлевается: иначе атакующий держал бы
        // владельца заблокированным бесконечно, продолжая попытки.
        Assert.Equal(attemptsBefore, context.User.FailedLoginAttempts);
        Assert.Equal(lockoutEndBefore, context.User.LockoutEnd);
        Assert.Equal(updateCallsBefore, context.Repository.UpdateCallCount);
    }

    [Fact]
    public async Task Login_WithCorrectPasswordAfterFailures_ResetsCounterAndRecordsLogin()
    {
        var context = CreateContext();
        context.Clock.UtcNow = LoginStart;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await context.Handler.Handle(
                new LoginUserCommand(context.User.Email, "wrong-password"),
                CancellationToken.None);
        }

        Assert.Equal(3, context.User.FailedLoginAttempts);

        var result = await context.Handler.Handle(
            new LoginUserCommand(context.User.Email, CorrectPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.User.FailedLoginAttempts);
        Assert.Null(context.User.LastFailedLoginAt);
        Assert.Null(context.User.LockoutEnd);
        Assert.NotNull(context.User.LastLoginAt);
        Assert.Equal(4, context.Repository.UpdateCallCount);
        Assert.Equal(context.User.Id, result.Value.Id);
    }

    private static async Task LockAccountAsync(TestContext context)
    {
        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            // Каждая попытка — на минуту позже: окно сброса (15 минут) не истекает,
            // поэтому счётчик накапливается.
            context.Clock.UtcNow = LoginStart.AddMinutes(attempt);

            var result = await context.Handler.Handle(
                new LoginUserCommand(context.User.Email, "wrong-password"),
                CancellationToken.None);

            AssertFailure(result, "auth.login.invalidCredentials");
        }

        Assert.True(context.User.IsLockedOut(context.Clock.UtcNow));
    }

    private static void AssertFailure(
        CSharpFunctionalExtensions.Result<AuthenticatedUser, Failure> result,
        string expectedCode)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Errors[0].Code);
    }

    private static TestContext CreateContext()
    {
        var user = User.Create(
            "user@example.com",
            "hash:" + CorrectPassword,
            "Ivan",
            "Petrov").Value;

        var repository = new FakeUserRepository();
        repository.Add(user);

        var clock = new FakeTimeProvider(LoginStart);
        var handler = new LoginUserHandler(
            repository,
            new FakePasswordService(),
            clock,
            NullLogger<LoginUserHandler>.Instance);

        return new TestContext(handler, repository, clock, user);
    }

    private sealed record TestContext(
        LoginUserHandler Handler,
        FakeUserRepository Repository,
        FakeTimeProvider Clock,
        User User);
}
