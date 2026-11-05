using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Подключение Serilog: консоль, файл и журнал запросов.
/// </summary>
/// <remarks>
/// Serilog — единственный логгер приложения: <c>builder.Host.UseSerilog</c> заменяет
/// <c>ILoggerFactory</c>, поэтому встроенные провайдеры (консоль, EventLog) больше не
/// используются, а уровень задаётся в одном месте — <see cref="LoggingLevels"/>.
/// <para>
/// Один шаблон для консоли и файла: строка журнала должна одинаково читаться в выводе
/// контейнера и в файле, а разбор по уровням и категориям возможен и без структурного
/// формата — уровень и <c>SourceContext</c> стоят в начале строки.
/// </para>
/// </remarks>
public static class SerilogExtensions
{
    /// <summary>
    /// Шаблон строки журнала: время с зоной, уровень, категория источника и исключение.
    /// </summary>
    public const string OUTPUT_TEMPLATE =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Префикс путей health-проб: в журнале запросов они пишутся только на уровне Verbose.
    /// </summary>
    private const string HEALTH_PATH_PREFIX = "/health";

    /// <summary>
    /// Подключает Serilog к хосту: консоль и, если включено, файл.
    /// </summary>
    /// <param name="builder">Построитель приложения.</param>
    /// <param name="fileSettings">Проверенные настройки файла (см. <see cref="FileLoggingSettings"/>).</param>
    public static void UseAppSerilog(this WebApplicationBuilder builder, FileLoggingSettings fileSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fileSettings);

        builder.Host.UseSerilog((context, configuration) =>
            ConfigureLogger(configuration, context.Configuration, fileSettings));
    }

    /// <summary>
    /// Собирает конфигурацию логгера: уровни из <c>Logging:LogLevel</c>, sink'и — консоль и файл.
    /// </summary>
    /// <param name="configuration">Конфигурация Serilog, которую дополняет метод.</param>
    /// <param name="appConfiguration">Конфигурация приложения с секцией <c>Logging:LogLevel</c>.</param>
    /// <param name="fileSettings">Настройки файла; при <c>Enabled=false</c> файл не пишется.</param>
    /// <returns>Та же конфигурация — для цепочки вызовов.</returns>
    /// <exception cref="InvalidOperationException">В <c>Logging:LogLevel</c> некорректный уровень.</exception>
    public static LoggerConfiguration ConfigureLogger(
        LoggerConfiguration configuration,
        IConfiguration appConfiguration,
        FileLoggingSettings fileSettings)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(appConfiguration);
        ArgumentNullException.ThrowIfNull(fileSettings);

        var levels = LoggingLevels.FromConfiguration(appConfiguration);

        configuration
            .MinimumLevel.ControlledBy(new LoggingLevelSwitch(levels.Default))
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: OUTPUT_TEMPLATE,
                formatProvider: CultureInfo.InvariantCulture);

        // Переопределения по категориям — после MinimumLevel: это части одного списка уровней,
        // а не отдельная настройка, поэтому они не могут перекрыть главный уровень.
        foreach (var (category, level) in levels.Overrides)
        {
            configuration.MinimumLevel.Override(category, level);
        }

        if (fileSettings.Enabled)
        {
            configuration.WriteTo.File(
                path: fileSettings.Path,
                rollingInterval: fileSettings.RollingInterval,
                retainedFileCountLimit: fileSettings.RetainedFileCountLimit,
                fileSizeLimitBytes: fileSettings.GetFileSizeLimitBytes(),
                rollOnFileSizeLimit: true,
                shared: fileSettings.Shared,
                outputTemplate: OUTPUT_TEMPLATE,
                formatProvider: CultureInfo.InvariantCulture);
        }

        return configuration;
    }

    /// <summary>
    /// Включает журнал запросов: одна запись на запрос с методом, путём, статусом и временем.
    /// </summary>
    /// <param name="app">Приложение, конвейер которого дополняется.</param>
    /// <remarks>
    /// Middleware ставится до <c>UseExceptionHandler</c> (см. <c>Program.cs</c>): обработчик
    /// исключений повторно выполняет конвейер, и журнал, стоящий ниже него, записал бы один
    /// запрос дважды. Health-пробы идут на уровне Verbose, чтобы не заслонять бизнес-запросы.
    /// </remarks>
    public static void UseAppRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
            {
                if (context.Request.Path.StartsWithSegments(HEALTH_PATH_PREFIX))
                {
                    return LogEventLevel.Verbose;
                }

                return exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : LogEventLevel.Information;
            };
        });
    }
}
