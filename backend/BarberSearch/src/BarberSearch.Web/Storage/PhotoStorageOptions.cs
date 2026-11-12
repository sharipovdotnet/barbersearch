namespace BarberSearch.Web.Storage;

/// <summary>
/// Настройки хранилища изображений из секции <c>Storage</c>: фотографий портфолио и аватаров
/// пользователей.
/// </summary>
/// <remarks>
/// <para>
/// В секции два провайдера. <see cref="PhotoStorageProvider.Local"/> пишет в каталог
/// <see cref="RootPath"/>; <see cref="PhotoStorageProvider.S3"/> пишет в бакет
/// <see cref="S3StorageOptions"/> и каталог не трогает. Предел размера файла — доменная
/// константа <c>Constants.MAX_PHOTO_BYTES</c>, а префикс публичного URL —
/// <c>Constants.PORTFOLIO_MEDIA_PATH_PREFIX</c>: второй источник правды в конфигурации
/// разошёлся бы с проверкой, которую видит клиент.
/// </para>
/// <para>
/// Для локального провайдера относительный путь считается от <c>ContentRootPath</c>,
/// абсолютный используется как есть. Каталог создаётся и проверяется на запись в
/// <see cref="FromConfiguration"/>, поэтому недоступный диск роняет старт с понятным
/// сообщением, а не первую загрузку: иначе ошибка выяснилась бы только тогда, когда барбер
/// уже выбрал файл. Для S3 проверяются обязательные поля, а существование бакета — на старте
/// хоста (<c>S3StorageInitializer</c>).
/// </para>
/// <para>
/// Раскладка общая для обоих провайдеров: <c>portfolio/{profileId:N}/…</c> и
/// <c>avatars/{userId:N}/…</c>, варианты — рядом с исходником. Файлы не попадают в backup
/// PostgreSQL, поэтому локальный каталог бэкапится отдельно целиком, а бакет — средствами
/// object storage. Несколько инстансов API работают с одним бакетом; общий диск — не решение.
/// </para>
/// </remarks>
public sealed record PhotoStorageOptions
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Путь локального провайдера по умолчанию относительно content root:
    /// <c>App_Data/photos</c>.
    /// </summary>
    public const string DEFAULT_ROOT_PATH = "App_Data/photos";

    /// <summary>
    /// Регион S3 по умолчанию: MinIO принимает любое значение, но подпись требует непустой регион.
    /// </summary>
    public const string DEFAULT_S3_REGION = "us-east-1";

    /// <summary>
    /// Провайдер хранилища; по умолчанию <see cref="PhotoStorageProvider.Local"/>.
    /// </summary>
    public PhotoStorageProvider Provider { get; init; } = PhotoStorageProvider.Local;

    /// <summary>
    /// Абсолютный путь к каталогу локального провайдера, в котором лежат файлы портфолио и аватаров.
    /// </summary>
    /// <remarks>
    /// После <see cref="FromConfiguration"/> значение абсолютное: относительный путь уже разрешён
    /// от content root, а каталог существует и доступен на запись. При
    /// <see cref="PhotoStorageProvider.S3"/> поле не используется и каталог не создаётся.
    /// </remarks>
    public string RootPath { get; init; } = DEFAULT_ROOT_PATH;

    /// <summary>
    /// Настройки S3-совместимого провайдера; читаются только при
    /// <see cref="PhotoStorageProvider.S3"/>.
    /// </summary>
    public S3StorageOptions S3 { get; init; } = new();

    /// <summary>
    /// Читает секцию, разбирает провайдера и проверяет его настройки.
    /// </summary>
    /// <remarks>
    /// Для <see cref="PhotoStorageProvider.Local"/> путь разрешается и проверяется пробным
    /// файлом; для <see cref="PhotoStorageProvider.S3"/> проверяются обязательные поля, а бакет —
    /// позже, в <c>S3StorageInitializer</c> (проверка требует сети и асинхронна).
    /// </remarks>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <returns>Настройки с разобранным провайдером и проверенными значениями.</returns>
    /// <exception cref="InvalidOperationException">
    /// Провайдер неизвестен, не задан обязательный ключ, путь некорректен либо каталог нельзя
    /// создать или записать в него.
    /// </exception>
    public static PhotoStorageOptions FromConfiguration(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var provider = ParseProvider(configuration[$"{SectionName}:Provider"]);
        var s3 = configuration.GetSection($"{SectionName}:S3").Get<S3StorageOptions>()
            ?? new S3StorageOptions();

        if (provider == PhotoStorageProvider.S3)
        {
            return new PhotoStorageOptions
            {
                Provider = provider,
                RootPath = DEFAULT_ROOT_PATH,
                S3 = ValidateS3(s3)
            };
        }

        // Локальный провайдер: путь по умолчанию тот же, что у отсутствующего ключа. Явно
        // пустое значение — ошибка конфигурации, а не повод молча взять дефолт.
        var configuredPath = configuration[$"{SectionName}:RootPath"] ?? DEFAULT_ROOT_PATH;
        var path = configuredPath.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                """
                Storage:RootPath is not configured.
                Set it to a writable directory, e.g.
                  Storage__RootPath=/app/App_Data/photos
                The directory is created at startup, and an unavailable disk fails the start
                instead of the first upload.
                """);
        }

        if (!Path.IsPathRooted(path))
        {
            try
            {
                path = Path.GetFullPath(path, environment.ContentRootPath);
            }
            catch (Exception exception)
                when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Storage:RootPath '{configuredPath}' is not a valid directory path.",
                    exception);
            }
        }

        EnsureWritableDirectory(path);

        return new PhotoStorageOptions { Provider = provider, RootPath = path, S3 = s3 };
    }

    /// <summary>
    /// Разбирает <c>Storage:Provider</c>: строка, а не число, чтобы <c>"1"</c> не превращался
    /// в <see cref="PhotoStorageProvider.S3"/>.
    /// </summary>
    /// <param name="value">Сырое значение конфигурации; <c>null</c> и пустая строка — Local.</param>
    /// <returns>Разобранный провайдер.</returns>
    /// <exception cref="InvalidOperationException">
    /// Значение не <c>Local</c> и не <c>S3</c> (регистр и окружающие пробелы не важны).
    /// </exception>
    public static PhotoStorageProvider ParseProvider(string? value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            return PhotoStorageProvider.Local;
        }

        if (string.Equals(normalized, nameof(PhotoStorageProvider.Local), StringComparison.OrdinalIgnoreCase))
        {
            return PhotoStorageProvider.Local;
        }

        if (string.Equals(normalized, nameof(PhotoStorageProvider.S3), StringComparison.OrdinalIgnoreCase))
        {
            return PhotoStorageProvider.S3;
        }

        throw new InvalidOperationException(
            $"Storage:Provider '{normalized}' is not supported. "
            + $"Allowed values: '{nameof(PhotoStorageProvider.Local)}' and "
            + $"'{nameof(PhotoStorageProvider.S3)}'.");
    }

    /// <summary>
    /// Проверяет обязательные поля S3 и нормализует значения; значения секретов в текст
    /// исключения не попадают.
    /// </summary>
    /// <param name="s3">Сырые настройки подсекции <c>Storage:S3</c>.</param>
    /// <returns>Нормализованные настройки: без пробелов и с регионом по умолчанию.</returns>
    /// <exception cref="InvalidOperationException">
    /// Не задан обязательный ключ или <c>ServiceUrl</c> не является абсолютным http(s)-адресом.
    /// </exception>
    private static S3StorageOptions ValidateS3(S3StorageOptions s3)
    {
        var normalized = s3 with
        {
            Bucket = s3.Bucket.Trim(),
            ServiceUrl = s3.ServiceUrl.Trim(),
            Region = string.IsNullOrWhiteSpace(s3.Region) ? DEFAULT_S3_REGION : s3.Region.Trim(),
            AccessKey = s3.AccessKey.Trim(),
            SecretKey = s3.SecretKey.Trim()
        };

        var missing = new List<string>(4);

        if (string.IsNullOrEmpty(normalized.Bucket))
        {
            missing.Add("Storage:S3:Bucket");
        }

        if (string.IsNullOrEmpty(normalized.ServiceUrl))
        {
            missing.Add("Storage:S3:ServiceUrl");
        }

        if (string.IsNullOrEmpty(normalized.AccessKey))
        {
            missing.Add("Storage:S3:AccessKey");
        }

        if (string.IsNullOrEmpty(normalized.SecretKey))
        {
            missing.Add("Storage:S3:SecretKey");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Storage:Provider=S3 requires {string.Join(", ", missing)}. "
                + "Set them through the environment (Storage__S3__*) or user secrets; "
                + "the values are never printed. The bucket is checked at startup, and an "
                + "unavailable bucket fails the start instead of the first upload.");
        }

        if (!Uri.TryCreate(normalized.ServiceUrl, UriKind.Absolute, out var serviceUri)
            || (serviceUri.Scheme != Uri.UriSchemeHttp && serviceUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Storage:S3:ServiceUrl is not a valid absolute http(s) URL. Set it to the S3 API "
                + "endpoint, e.g. http://localhost:9000 for MinIO (the web console lives on 9001).");
        }

        return normalized;
    }

    /// <summary>
    /// Создаёт каталог хранилища и проверяет его запись пробным файлом.
    /// </summary>
    /// <param name="directoryPath">Абсолютный путь к каталогу.</param>
    private static void EnsureWritableDirectory(string directoryPath)
    {
        try
        {
            var directory = Directory.CreateDirectory(directoryPath);
            var probePath = Path.Combine(directory.FullName, $".write-probe-{Guid.NewGuid():N}");

            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException
                  or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Storage:RootPath '{directoryPath}' is not writable: the directory cannot be "
                + "created or written. Mount a writable volume for the photo directory, or point "
                + "Storage:RootPath at an available one. Photo files are not stored in PostgreSQL "
                + "and must be backed up separately.",
                exception);
        }
    }
}
