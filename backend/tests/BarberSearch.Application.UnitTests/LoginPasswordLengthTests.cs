using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Application.Users.Passwords;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Потолок длины пароля при входе: композиционные правила к существующим паролям не применяются.
/// </summary>
public class LoginPasswordLengthTests
{
    [Fact]
    public async Task Login_WithPasswordLongerThanMaximum_RejectsWithoutHashing()
    {
        var passwords = new FakePasswordService();
        var handler = new LoginUserHandler(
            new FakeUserRepository(),
            passwords,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<LoginUserHandler>.Instance);

        var result = await handler.Handle(
            new LoginUserCommand(
                "user@example.com",
                new string('x', PasswordPolicy.MAX_LENGTH + 1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.login.invalidCredentials", result.Error.Errors[0].Code);

        // Ни одного обращения к PBKDF2: ни поиска фиктивного хеша, ни сверки.
        Assert.Equal(0, passwords.HashCallCount);
        Assert.Equal(0, passwords.VerifyCallCount);
        Assert.Equal(0, passwords.VerifyDummyCallCount);
    }
}
