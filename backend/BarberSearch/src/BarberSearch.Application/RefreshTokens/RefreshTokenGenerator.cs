using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace BarberSearch.Application.RefreshTokens;

/// <summary>
/// Генерация открытого refresh-токена и его SHA-256.
/// </summary>
/// <remarks>
/// Открытый токен — 64 криптографически случайных байта в base64url (86 символов).
/// В базу попадает только <see cref="ComputeHash"/>: 64 шестнадцатеричных символа.
/// </remarks>
public static class RefreshTokenGenerator
{
    /// <summary>
    /// Количество случайных байт в открытом токене.
    /// </summary>
    public const int TOKEN_BYTES = 64;

    /// <summary>
    /// Создаёт новый открытый токен.
    /// </summary>
    public static string CreateToken()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TOKEN_BYTES));

    /// <summary>
    /// Считает SHA-256 открытого токена в нижнем регистре.
    /// </summary>
    public static string ComputeHash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();
    }
}
