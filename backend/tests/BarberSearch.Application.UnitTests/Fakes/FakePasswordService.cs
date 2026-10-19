using BarberSearch.Application.Users;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Детерминированная замена <see cref="IPasswordService"/>: хеш — это префикс и открытый пароль.
/// </summary>
public sealed class FakePasswordService : IPasswordService
{
    public string Hash(string password) => $"hash:{password}";

    public PasswordVerification Verify(string passwordHash, string password)
        => passwordHash == Hash(password)
            ? PasswordVerification.Success
            : PasswordVerification.Failed;

    public PasswordVerification VerifyDummy(string password) => PasswordVerification.Failed;
}
