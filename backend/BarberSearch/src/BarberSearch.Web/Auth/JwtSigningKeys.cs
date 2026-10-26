using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Ключи подписи и проверки JWT: текущий и предыдущие.
/// </summary>
/// <remarks>
/// <c>kid</c> выводится из самого ключа (первые <see cref="KEY_ID_LENGTH"/> hex SHA-256). Это не раскрывает ключ,
/// но позволяет отличить токены, подписанные старым и новым ключом, во время ротации.
/// <para>
/// Новые токены подписываются только текущим ключом; проверка принимает текущий и все
/// <see cref="AuthOptions.PreviousSigningKeys"/>. Предыдущий ключ убирают после того, как
/// истекут все выданные им access-токены (15 минут) и cookie (8 часов).
/// </para>
/// </remarks>
public sealed class JwtSigningKeys
{
    /// <summary>
    /// Длина <c>kid</c> в hex-символах: первые байты SHA-256 от ключа.
    /// </summary>
    private const int KEY_ID_LENGTH = 16;

    public JwtSigningKeys(AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var currentKey = CreateKey(options.SigningKey);

        var validationKeys = new List<SecurityKey>(options.PreviousSigningKeys.Length + 1)
        {
            currentKey
        };

        foreach (var previousKey in options.PreviousSigningKeys)
        {
            validationKeys.Add(CreateKey(previousKey));
        }

        Credentials = new SigningCredentials(currentKey, SecurityAlgorithms.HmacSha256);
        ValidationKeys = validationKeys.AsReadOnly();
    }

    /// <summary>
    /// Учётные данные для подписи новых токенов.
    /// </summary>
    public SigningCredentials Credentials { get; }

    /// <summary>
    /// Ключи, которыми принимаются токены: текущий и предыдущие.
    /// </summary>
    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    private static SymmetricSecurityKey CreateKey(string value)
        => new(Encoding.UTF8.GetBytes(value))
        {
            KeyId = ComputeKeyId(value)
        };

    private static string ComputeKeyId(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..KEY_ID_LENGTH].ToLowerInvariant();
}
