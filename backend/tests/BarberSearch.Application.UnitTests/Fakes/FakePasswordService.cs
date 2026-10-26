using BarberSearch.Application.Users;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Детерминированная замена <see cref="IPasswordService"/>: хеш — это префикс и открытый пароль.
/// </summary>
/// <remarks>
/// Счётчики вызовов нужны тестам, которые проверяют, что невалидный ввод не доходит
/// до хеширования: <see cref="Hash"/> не вызывается ни напрямую, ни через <see cref="Verify"/>.
/// </remarks>
public sealed class FakePasswordService : IPasswordService
{
    /// <summary>
    /// Сколько раз вычислялся хеш.
    /// </summary>
    public int HashCallCount { get; private set; }

    /// <summary>
    /// Сколько раз сверялся пароль с хешем.
    /// </summary>
    public int VerifyCallCount { get; private set; }

    /// <summary>
    /// Сколько раз выполнялась проверка по фиктивному хешу.
    /// </summary>
    public int VerifyDummyCallCount { get; private set; }

    public string Hash(string password)
    {
        HashCallCount++;

        return $"hash:{password}";
    }

    public PasswordVerification Verify(string passwordHash, string password)
    {
        VerifyCallCount++;

        return passwordHash == $"hash:{password}"
            ? PasswordVerification.Success
            : PasswordVerification.Failed;
    }

    public PasswordVerification VerifyDummy(string password)
    {
        VerifyDummyCallCount++;

        return PasswordVerification.Failed;
    }
}
