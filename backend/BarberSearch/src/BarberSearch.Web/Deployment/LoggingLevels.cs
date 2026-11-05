using Serilog.Events;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Минимальные уровни логирования Serilog, прочитанные из секции <c>Logging:LogLevel</c>.
/// </summary>
/// <remarks>
/// Уровни остаются в стандартной секции ASP.NET Core (R-13): её проверяет тест
/// <c>LoggingConfigurationTests</c>, и переменные окружения вида
/// <c>Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning</c> продолжают работать —
/// ими пользуются <c>deploy/smoke.ps1</c> и <c>load/*.ps1</c>.
/// <para>
/// Serilog получает уровни программно и <c>Serilog:MinimumLevel</c> не читает: два источника
/// истины разошлись бы при первой правке. Имена уровней переводятся из пространства
/// <c>Microsoft.Extensions.Logging</c> в <see cref="LogEventLevel"/>: <c>Trace</c> — это
/// <c>Verbose</c>, <c>Critical</c> — <c>Fatal</c>. Уровня «выключено» у Serilog нет, поэтому
/// <c>None</c> отклоняется на старте с объяснением, а не превращается молча в другой уровень.
/// </para>
/// </remarks>
public sealed record LoggingLevels
{
    /// <summary>
    /// Имя секции уровней.
    /// </summary>
    public const string SectionName = "Logging:LogLevel";

    private const string DEFAULT_CATEGORY = "Default";
    private const LogEventLevel DEFAULT_LEVEL = LogEventLevel.Information;

    /// <summary>
    /// Уровень категорий, для которых нет отдельной настройки.
    /// </summary>
    public LogEventLevel Default { get; init; } = DEFAULT_LEVEL;

    /// <summary>
    /// Уровни отдельных категорий или их префиксов, например <c>Microsoft.AspNetCore</c>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, LogEventLevel>> Overrides { get; init; } = [];

    /// <summary>
    /// Читает секцию <c>Logging:LogLevel</c>.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Уровень по умолчанию и переопределения по категориям.</returns>
    /// <exception cref="InvalidOperationException">
    /// В секции неизвестное имя уровня или <c>None</c>.
    /// </exception>
    public static LoggingLevels FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var defaultLevel = DEFAULT_LEVEL;
        var overrides = new List<KeyValuePair<string, LogEventLevel>>();

        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var level = ParseLevel(entry.Key, entry.Value);

            if (string.Equals(entry.Key, DEFAULT_CATEGORY, StringComparison.OrdinalIgnoreCase))
            {
                defaultLevel = level;
                continue;
            }

            overrides.Add(new KeyValuePair<string, LogEventLevel>(entry.Key, level));
        }

        return new LoggingLevels { Default = defaultLevel, Overrides = overrides };
    }

    private static LogEventLevel ParseLevel(string category, string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "critical" => LogEventLevel.Fatal,
            "none" => throw new InvalidOperationException(
                $"""
                {SectionName}:{category} = None is not supported: Serilog has no "off" level,
                so a category cannot be disabled. Remove the category to inherit
                {SectionName}:Default, or set it to Fatal.
                """),
            _ => throw new InvalidOperationException(
                $"""
                {SectionName}:{category} has an unknown level '{value}'.
                Allowed values: Trace, Debug, Information, Warning, Error, Critical.
                Serilog has no "None" level, so a category cannot be disabled.
                """)
        };
    }
}
