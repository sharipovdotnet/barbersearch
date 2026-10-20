using System.Globalization;
using BarberSearch.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Проверенная на старте конфигурация деплоя.
/// </summary>
/// <remarks>
/// Все обязательные настройки читаются один раз, до регистрации сервисов и запуска хоста.
/// Сообщения об ошибках называют переменные окружения и пути, но никогда — значения секретов.
/// Отдельные проверки (строка подключения, ключ подписи, key ring, CORS, прокси) живут рядом
/// с соответствующими настройками; этот тип только собирает их в один вызов.
/// </remarks>
public sealed record StartupSettings
{
    /// <summary>
    /// Строка подключения PostgreSQL.
    /// </summary>
    public required string PostgresConnectionString { get; init; }

    /// <summary>
    /// Проверенные настройки аутентификации.
    /// </summary>
    public required AuthOptions Auth { get; init; }

    /// <summary>
    /// Настройки Data Protection вместе с проверенным каталогом key ring.
    /// </summary>
    public required DataProtectionSettings DataProtection { get; init; }

    /// <summary>
    /// Настройки CORS.
    /// </summary>
    public required CorsSettings Cors { get; init; }

    /// <summary>
    /// Настройки forwarded headers.
    /// </summary>
    public required ForwardedHeadersSettings ForwardedHeaders { get; init; }

    /// <summary>
    /// Таймаут graceful shutdown в секундах.
    /// </summary>
    public required int ShutdownTimeoutSeconds { get; init; }

    /// <summary>
    /// Таймаут проверки доступности БД в readiness-пробе.
    /// </summary>
    public required int HealthDatabaseTimeoutSeconds { get; init; }

    /// <summary>
    /// Читает и проверяет конфигурацию.
    /// </summary>
    /// <exception cref="InvalidOperationException">Обязательная настройка отсутствует или противоречива.</exception>
    public static StartupSettings Load(IConfiguration configuration, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var connectionString = configuration.GetConnectionString(
            BarberSearch.Web.DependencyInjection.POSTGRES_CONNECTION_STRING_NAME);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                """
                Connection string 'Postgres' is not configured.
                Set ConnectionStrings:Postgres in appsettings.json, user secrets, or the
                environment variable ConnectionStrings__Postgres.
                """);
        }

        var auth = AuthOptions.FromConfiguration(configuration);
        var cors = CorsSettings.FromConfiguration(configuration);
        var forwardedHeaders = ForwardedHeadersSettings.FromConfiguration(configuration);
        var dataProtection = DataProtectionSettings.FromConfiguration(configuration, environment);

        // SameSite=None без CORS не имеет смысла: cross-site SPA не сможет вызвать API,
        // а cookie будет отправляться в более широком, чем нужно, контексте.
        if (auth.GetSameSiteMode() == SameSiteMode.None && !cors.Enabled)
        {
            throw new InvalidOperationException(
                """
                Auth:CookieSameSite=None is only valid for a cross-site SPA.
                Configure Cors:AllowedOrigins with the SPA origin, or use Lax/Strict.
                """);
        }

        return new StartupSettings
        {
            PostgresConnectionString = connectionString,
            Auth = auth,
            DataProtection = dataProtection,
            Cors = cors,
            ForwardedHeaders = forwardedHeaders,
            ShutdownTimeoutSeconds = ReadInt(
                configuration,
                "Deployment:ShutdownTimeoutSeconds",
                defaultValue: 30,
                min: 5,
                max: 300),
            HealthDatabaseTimeoutSeconds = ReadInt(
                configuration,
                "HealthChecks:DatabaseTimeoutSeconds",
                defaultValue: 5,
                min: 1,
                max: 60)
        };
    }

    private static int ReadInt(
        IConfiguration configuration,
        string key,
        int defaultValue,
        int min,
        int max)
    {
        var raw = configuration[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < min
            || value > max)
        {
            throw new InvalidOperationException($"{key} must be an integer between {min} and {max}.");
        }

        return value;
    }
}
