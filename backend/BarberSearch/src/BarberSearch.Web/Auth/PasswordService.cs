using BarberSearch.Application.Users;
using BarberSearch.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Обёртка над <see cref="IPasswordHasher{TUser}"/>: Application проверяет пароль,
/// но не знает, каким алгоритмом он захеширован.
/// </summary>
/// <remarks>
/// Формат — IdentityV3 (PBKDF2-HMAC-SHA256, соль на пользователя). Если в базе окажется хеш
/// в другом формате, <c>VerifyHashedPassword</c> вернёт <c>Failed</c>; смена формата делается
/// через <c>SuccessRehashNeeded</c> и перезапись хеша при входе.
/// </remarks>
public sealed class PasswordService : IPasswordService
{
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(null!, "not-a-real-password");

    private readonly IPasswordHasher<User> _hasher;

    public PasswordService(IPasswordHasher<User> hasher)
    {
        _hasher = hasher;
    }

    /// <inheritdoc />
    public string Hash(string password)
        => _hasher.HashPassword(null!, password);

    /// <inheritdoc />
    public PasswordVerification Verify(string passwordHash, string password)
        => _hasher.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed
        };

    /// <inheritdoc />
    public PasswordVerification VerifyDummy(string password)
        => Verify(DummyHash, password);
}
