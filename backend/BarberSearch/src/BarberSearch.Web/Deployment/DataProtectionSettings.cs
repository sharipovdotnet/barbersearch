using System.Security.Cryptography.X509Certificates;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки Data Protection из секции <c>DataProtection</c>.
/// </summary>
/// <remarks>
/// Ключи cookie-аутентификации, antiforgery и refresh-cookie должны переживать перезапуск
/// процесса и быть общими для всех инстансов. Поэтому каталог key ring задаётся явно
/// (<c>DataProtection:KeysPath</c> / <c>DataProtection__KeysPath</c>) и монтируется как
/// постоянный том; в git и в образ он не попадает.
/// <para>
/// Шифрование ключей на диске опционально: если задан <see cref="CertificatePath"/>,
/// key ring шифруется сертификатом (PKCS#12), а пароль приходит из секрет-стора.
/// Сам сертификат в репозитории не хранится.
/// </para>
/// </remarks>
public sealed record DataProtectionSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Имя приложения Data Protection. Одинаковое у всех инстансов и не меняется при ротации.
    /// </summary>
    public const string ApplicationName = "BarberSearch";

    /// <summary>
    /// Каталог key ring: абсолютный путь или путь относительно content root.
    /// </summary>
    public string KeysPath { get; init; } = string.Empty;

    /// <summary>
    /// Путь к PKCS#12-сертификату для шифрования ключей. Необязателен.
    /// </summary>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// Пароль сертификата. Приходит из секрет-стора, в конфигурации не хранится.
    /// </summary>
    public string? CertificatePassword { get; init; }

    /// <summary>
    /// Читает секцию, проверяет путь и доступность каталога на запись.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <exception cref="InvalidOperationException">
    /// Путь не задан, каталог недоступен для записи или настройки сертификата противоречивы.
    /// </exception>
    public static DataProtectionSettings FromConfiguration(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var settings = configuration
            .GetSection(SectionName)
            .Get<DataProtectionSettings>() ?? new DataProtectionSettings();

        var keysPath = settings.KeysPath?.Trim();

        if (string.IsNullOrWhiteSpace(keysPath))
        {
            throw new InvalidOperationException(
                """
                DataProtection:KeysPath is not configured.
                Set it to a persistent volume shared by every API instance, e.g.
                DataProtection__KeysPath=/var/lib/barbersearch/data-protection.
                The directory must not be inside the application image.
                """);
        }

        if (!Path.IsPathRooted(keysPath))
        {
            keysPath = Path.GetFullPath(keysPath, environment.ContentRootPath);
        }

        EnsureWritableDirectory(keysPath);

        var certificatePath = settings.CertificatePath?.Trim();

        if (string.IsNullOrWhiteSpace(certificatePath) && !string.IsNullOrEmpty(settings.CertificatePassword))
        {
            throw new InvalidOperationException(
                "DataProtection:CertificatePassword is set, but DataProtection:CertificatePath is empty.");
        }

        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            if (!Path.IsPathRooted(certificatePath))
            {
                certificatePath = Path.GetFullPath(certificatePath, environment.ContentRootPath);
            }

            if (!File.Exists(certificatePath))
            {
                throw new InvalidOperationException(
                    $"DataProtection:CertificatePath '{certificatePath}' does not exist.");
            }
        }

        return settings with
        {
            KeysPath = keysPath,
            CertificatePath = string.IsNullOrWhiteSpace(certificatePath) ? null : certificatePath
        };
    }

    /// <summary>
    /// Каталог key ring для <c>PersistKeysToFileSystem</c>.
    /// </summary>
    public DirectoryInfo GetKeysDirectory() => new(KeysPath);

    /// <summary>
    /// Загружает сертификат шифрования ключей, если он настроен.
    /// </summary>
    /// <returns>Сертификат с закрытым ключом или <c>null</c>, если шифрование выключено.</returns>
    /// <exception cref="InvalidOperationException">Сертификат не содержит закрытого ключа.</exception>
    public X509Certificate2? LoadCertificate()
    {
        if (string.IsNullOrWhiteSpace(CertificatePath))
        {
            return null;
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            CertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);

        if (certificate.HasPrivateKey)
        {
            return certificate;
        }

        certificate.Dispose();

        throw new InvalidOperationException(
            "DataProtection:CertificatePath must point to a certificate with a private key.");
    }

    private static void EnsureWritableDirectory(string path)
    {
        try
        {
            var directory = Directory.CreateDirectory(path);
            var probePath = Path.Combine(directory.FullName, $".write-probe-{Guid.NewGuid():N}");

            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"DataProtection:KeysPath '{path}' is not writable. " +
                "Mount a persistent, writable volume for the Data Protection key ring.",
                exception);
        }
    }
}
