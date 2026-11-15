using System.Text.Json;

namespace BarberSearch.Application.Push;

/// <summary>
/// Ключ сервисного аккаунта Google — то, из чего собирается OAuth-токен для FCM HTTP v1.
/// </summary>
/// <remarks>
/// <para>
/// Ключ приходит из <c>Push:ServiceAccountJson</c> или из файла по
/// <c>Push:ServiceAccountPath</c>; разбор — единственное место, где проверяется, что строка
/// действительно похожа на сервисный аккаунт. <see cref="PushOptions.IsConfigured"/> опирается
/// на этот разбор, поэтому «битый ключ» — это выключенный канал, а не падение старта.
/// </para>
/// <para>
/// <see cref="PrivateKeyPem"/> — секрет. Ни <c>ToString</c>, ни логи не печатают ни ключ,
/// ни адрес сервисного аккаунта: наружу выходит только факт «ключ разобран».
/// </para>
/// </remarks>
public sealed class ServiceAccountKey
{
    /// <summary>
    /// Адрес OAuth-токена Google, если в ключе он не указан.
    /// </summary>
    public const string DEFAULT_TOKEN_URI = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Адрес сервисного аккаунта (<c>client_email</c>) — он же <c>iss</c> в JWT.
    /// </summary>
    public string ClientEmail { get; }

    /// <summary>
    /// Приватный ключ в PEM (PKCS#8). Секрет: используется только для подписи JWT.
    /// </summary>
    public string PrivateKeyPem { get; }

    /// <summary>
    /// Адрес OAuth-токена (<c>token_uri</c>) — он же <c>aud</c> в JWT.
    /// </summary>
    public string TokenUri { get; }

    /// <summary>
    /// Идентификатор проекта из ключа; может отсутствовать.
    /// </summary>
    public string? ProjectId { get; }

    private ServiceAccountKey(
        string clientEmail,
        string privateKeyPem,
        string tokenUri,
        string? projectId)
    {
        ClientEmail = clientEmail;
        PrivateKeyPem = privateKeyPem;
        TokenUri = tokenUri;
        ProjectId = projectId;
    }

    /// <summary>
    /// Разбирает JSON сервисного аккаунта.
    /// </summary>
    /// <remarks>
    /// Проверяются только поля, без которых невозможна подпись: тип (если указан),
    /// <c>client_email</c> и <c>private_key</c>. <c>token_uri</c> необязателен — по умолчанию
    /// берётся <see cref="DEFAULT_TOKEN_URI"/>; <c>project_id</c> тоже необязателен, потому
    /// что проект для URL приходит отдельным ключом <c>Push:ProjectId</c>.
    /// </remarks>
    /// <param name="json">JSON сервисного аккаунта или <c>null</c>.</param>
    /// <param name="key">Разобранный ключ; при <c>false</c> — <c>null</c>.</param>
    /// <returns><c>true</c>, если ключ пригоден для подписи.</returns>
    public static bool TryParse(string? json, out ServiceAccountKey? key)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var type = ReadString(root, "type");
            if (type is not null && !string.Equals(type, "service_account", StringComparison.Ordinal))
            {
                return false;
            }

            var clientEmail = ReadString(root, "client_email");
            var privateKeyPem = ReadString(root, "private_key");

            if (string.IsNullOrWhiteSpace(clientEmail)
                || !clientEmail.Contains('@', StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(privateKeyPem)
                || !privateKeyPem.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal))
            {
                return false;
            }

            var tokenUri = ReadString(root, "token_uri");

            key = new ServiceAccountKey(
                clientEmail.Trim(),
                privateKeyPem,
                string.IsNullOrWhiteSpace(tokenUri) ? DEFAULT_TOKEN_URI : tokenUri.Trim(),
                ReadString(root, "project_id"));

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Печатает только факт наличия ключа: ни PEM, ни адрес сервисного аккаунта в журнал
    /// и в исключения не попадают.
    /// </summary>
    public override string ToString() => "ServiceAccountKey { configured }";

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
