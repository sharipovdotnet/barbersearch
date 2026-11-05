using Serilog;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки записи журнала в файл из секции <c>Logging:File</c>.
/// </summary>
/// <remarks>
/// Файл — единственное место, где журнал переживает перезапуск процесса, поэтому путь
/// разрешается от content root, а каталог проверяется на запись до запуска хоста: иначе
/// ошибка пути всплыла бы только при первой записи и молча (Serilog by default не сообщает
/// об ошибках sink'а).
/// <para>
/// Ротация суточная, хранятся последние <see cref="RetainedFileCountLimit"/> файлов;
/// <see cref="FileSizeLimitMegabytes"/> ограничивает размер одного файла, чтобы всплеск
/// ошибок не занял диск целиком до конца суток.
/// </para>
/// <para>
/// <see cref="Shared"/> нужен, когда несколько инстансов пишут в один каталог (общий том):
/// без него второй процесс молча не сможет открыть файл. По умолчанию выключено — один
/// инстанс пишет быстрее и не пересекается с другими.
/// </para>
/// </remarks>
public sealed record FileLoggingSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Logging:File";

    private const int MIN_RETAINED_FILE_COUNT = 1;
    private const int MAX_RETAINED_FILE_COUNT = 365;
    private const int MIN_FILE_SIZE_LIMIT_MEGABYTES = 1;
    private const int MAX_FILE_SIZE_LIMIT_MEGABYTES = 1024;

    /// <summary>
    /// Включает запись в файл. Консоль настраивается отдельно и остаётся всегда.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Путь к файлу журнала: абсолютный или относительный (тогда — от content root).
    /// </summary>
    public string Path { get; init; } = "logs/barbersearch-.log";

    /// <summary>
    /// Интервал ротации. <see cref="RollingInterval.Infinite"/> не поддерживается:
    /// хранить фиксированное число файлов без ротации нельзя.
    /// </summary>
    public RollingInterval RollingInterval { get; init; } = RollingInterval.Day;

    /// <summary>
    /// Сколько последних файлов журнала хранить.
    /// </summary>
    public int RetainedFileCountLimit { get; init; } = 14;

    /// <summary>
    /// Предельный размер одного файла в мегабайтах, после которого Serilog открывает следующий.
    /// </summary>
    public int FileSizeLimitMegabytes { get; init; } = 50;

    /// <summary>
    /// Разрешает нескольким процессам писать в один и тот же файл.
    /// </summary>
    public bool Shared { get; init; }

    /// <summary>
    /// Предельный размер файла в байтах — в таком виде его ожидает Serilog.
    /// </summary>
    /// <returns>Размер в байтах.</returns>
    public long GetFileSizeLimitBytes() => FileSizeLimitMegabytes * 1024L * 1024L;

    /// <summary>
    /// Читает секцию, разрешает путь и проверяет, что каталог доступен на запись.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <returns>Настройки с абсолютным путём к файлу журнала.</returns>
    /// <exception cref="InvalidOperationException">
    /// Путь не задан, не является корректным путём, каталог недоступен на запись или параметры
    /// ротации противоречивы.
    /// </exception>
    public static FileLoggingSettings FromConfiguration(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var settings = configuration.GetSection(SectionName).Get<FileLoggingSettings>()
            ?? new FileLoggingSettings();

        // Выключенная запись в файл не должна ни создавать каталог, ни проверять его права:
        // в этом режиме единственным sink'ом остаётся консоль.
        if (!settings.Enabled)
        {
            return settings;
        }

        ValidateRolling(settings);

        var path = settings.Path?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                """
                Logging:File:Path is not configured.
                Set it to a file inside a writable directory, e.g.
                  Logging__File__Path=/app/logs/barbersearch-.log
                or disable file logging with Logging:File:Enabled=false.
                """);
        }

        if (!System.IO.Path.IsPathRooted(path))
        {
            try
            {
                path = System.IO.Path.GetFullPath(path, environment.ContentRootPath);
            }
            catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Logging:File:Path '{settings.Path}' is not a valid file path.",
                    exception);
            }
        }

        EnsureWritableDirectory(path);

        return settings with { Path = path };
    }

    private static void ValidateRolling(FileLoggingSettings settings)
    {
        if (!Enum.IsDefined(settings.RollingInterval))
        {
            throw new InvalidOperationException(
                $"Logging:File:RollingInterval has an unknown value. Use one of: " +
                $"{string.Join(", ", Enum.GetNames<RollingInterval>())}.");
        }

        if (settings.RollingInterval == Serilog.RollingInterval.Infinite)
        {
            throw new InvalidOperationException(
                """
                Logging:File:RollingInterval=Infinite is not supported: the log directory keeps
                a bounded number of files, which requires a rolling interval. Use Day (default),
                Hour, Month or Year.
                """);
        }

        if (settings.RetainedFileCountLimit < MIN_RETAINED_FILE_COUNT
            || settings.RetainedFileCountLimit > MAX_RETAINED_FILE_COUNT)
        {
            throw new InvalidOperationException(
                $"{SectionName}:RetainedFileCountLimit must be between " +
                $"{MIN_RETAINED_FILE_COUNT} and {MAX_RETAINED_FILE_COUNT}.");
        }

        if (settings.FileSizeLimitMegabytes < MIN_FILE_SIZE_LIMIT_MEGABYTES
            || settings.FileSizeLimitMegabytes > MAX_FILE_SIZE_LIMIT_MEGABYTES)
        {
            throw new InvalidOperationException(
                $"{SectionName}:FileSizeLimitMegabytes must be between " +
                $"{MIN_FILE_SIZE_LIMIT_MEGABYTES} and {MAX_FILE_SIZE_LIMIT_MEGABYTES}.");
        }
    }

    private static void EnsureWritableDirectory(string filePath)
    {
        var directoryPath = System.IO.Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(directoryPath))
        {
            return;
        }

        try
        {
            var directory = Directory.CreateDirectory(directoryPath);
            var probePath = System.IO.Path.Combine(directory.FullName, $".write-probe-{Guid.NewGuid():N}");

            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Logging:File:Path '{filePath}' is not writable: the directory '{directoryPath}' " +
                "cannot be created or written. Mount a writable volume for the log directory, " +
                "or disable file logging with Logging:File:Enabled=false.",
                exception);
        }
    }
}
