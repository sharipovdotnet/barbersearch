namespace BarberSearch.Web.Storage;

/// <summary>
/// Настройки локального хранилища фотографий портфолио из секции <c>Storage</c>.
/// </summary>
/// <remarks>
/// <para>
/// В секции есть только <see cref="RootPath"/>. Предел размера файла — доменная константа
/// <c>Constants.MAX_PHOTO_BYTES</c>, а префикс публичного URL —
/// <c>Constants.PORTFOLIO_MEDIA_PATH_PREFIX</c>: второй источник правды в конфигурации
/// разошёлся бы с проверкой, которую видит клиент.
/// </para>
/// <para>
/// Относительный путь считается от <c>ContentRootPath</c>, абсолютный используется как есть.
/// Каталог создаётся и проверяется на запись в <see cref="FromConfiguration"/>, поэтому
/// недоступный диск роняет старт с понятным сообщением, а не первую загрузку: иначе ошибка
/// выяснилась бы только тогда, когда барбер уже выбрал файл.
/// </para>
/// <para>
/// Один инстанс API и один диск: общий volume или object storage — отдельный срез.
/// Фотографии не попадают в backup PostgreSQL, каталог бэкапится отдельно.
/// </para>
/// </remarks>
public sealed record PhotoStorageOptions
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Путь по умолчанию относительно content root: <c>App_Data/photos</c>.
    /// </summary>
    public const string DEFAULT_ROOT_PATH = "App_Data/photos";

    /// <summary>
    /// Абсолютный путь к каталогу, в котором лежат файлы портфолио.
    /// </summary>
    /// <remarks>
    /// После <see cref="FromConfiguration"/> значение всегда абсолютное: относительный путь
    /// уже разрешён от content root. Сам каталог к этому моменту существует и доступен на запись.
    /// </remarks>
    public string RootPath { get; init; } = DEFAULT_ROOT_PATH;

    /// <summary>
    /// Читает секцию, разрешает путь и создаёт каталог, проверяя его на запись.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <returns>Настройки с абсолютным путём к каталогу хранилища.</returns>
    /// <exception cref="InvalidOperationException">
    /// Путь не задан, не является корректным путём либо каталог нельзя создать или записать в него.
    /// </exception>
    public static PhotoStorageOptions FromConfiguration(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var settings = configuration.GetSection(SectionName).Get<PhotoStorageOptions>()
            ?? new PhotoStorageOptions();

        var path = settings.RootPath?.Trim() ?? string.Empty;

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
                    $"Storage:RootPath '{settings.RootPath}' is not a valid directory path.",
                    exception);
            }
        }

        EnsureWritableDirectory(path);

        return settings with { RootPath = path };
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
