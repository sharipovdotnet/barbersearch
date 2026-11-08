using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace BarberSearch.Application.EmailVerifications;

/// <summary>
/// Генерация открытого токена подтверждения email и его SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// Открытый токен — 32 криптографически случайных байта в base64url (43 символа). В базу
/// попадает только <see cref="ComputeHash"/>: 64 шестнадцатеричных символа в нижнем регистре.
/// </para>
/// <para>
/// Отдельный класс, а не расширение <c>RefreshTokenGenerator</c>: у токенов разная длина и
/// разный контракт (refresh-токен живёт в cookie и цепочке ротации, этот — одноразовая
/// ссылка в письме). Общий рефакторинг смешал бы два независимых сценария.
/// </para>
/// </remarks>
public static class EmailVerificationTokenGenerator
{
    /// <summary>
    /// Количество случайных байт в открытом токене.
    /// </summary>
    public const int TOKEN_BYTES = 32;

    /// <summary>
    /// Длина открытого токена в символах base64url.
    /// </summary>
    public const int TOKEN_LENGTH = 43;

    /// <summary>
    /// Длина SHA-256 в шестнадцатеричной записи.
    /// </summary>
    public const int HASH_LENGTH = 64;

    /// <summary>
    /// Максимальная длина входной строки до хеширования.
    /// </summary>
    /// <remarks>
    /// Открытый токен всегда 43 символа; запас до 128 нужен только для того, чтобы длинный
    /// мусор отсекался проверкой формата, а не хешировался.
    /// </remarks>
    public const int MAX_TOKEN_LENGTH = 128;

    /// <summary>
    /// Создаёт новый открытый токен.
    /// </summary>
    public static string Generate()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TOKEN_BYTES));

    /// <summary>
    /// Проверяет формат открытого токена до хеширования: непустая строка не длиннее
    /// <see cref="MAX_TOKEN_LENGTH"/> символов из алфавита base64url (<c>[A-Za-z0-9_-]</c>).
    /// </summary>
    /// <remarks>
    /// Проверка синтаксическая, а не проверка подлинности: существование токена решается
    /// поиском по хешу. Мусорная строка отсекается здесь, чтобы не тратить SHA-256 и не
    /// ходить в базу с заведомо неверным значением.
    /// </remarks>
    public static bool IsValidToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MAX_TOKEN_LENGTH)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Считает SHA-256 открытого токена в нижнем регистре.
    /// </summary>
    /// <exception cref="ArgumentException">Токен не проходит <see cref="IsValidToken"/>.</exception>
    public static string ComputeHash(string token)
    {
        if (!IsValidToken(token))
        {
            throw new ArgumentException(
                "Token must be a non-empty base64url string of at most 128 characters.",
                nameof(token));
        }

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();
    }
}
