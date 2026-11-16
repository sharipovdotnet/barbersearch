using System.Globalization;
using BarberSearch.Application.Addresses;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Push;
using BarberSearch.Web.Auth;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Проверенная на старте конфигурация деплоя.
/// </summary>
/// <remarks>
/// Все обязательные настройки читаются один раз, до регистрации сервисов и запуска хоста.
/// Сообщения об ошибках называют переменные окружения и пути, но никогда — значения секретов.
/// Отдельные проверки (строка подключения, ключ подписи, key ring, CORS, прокси) живут рядом
/// с соответствующими настройками; этот тип только собирает их в один вызов.
/// В <c>Production</c> здесь же проверяется <c>AllowedHosts</c>: пустой список и <c>*</c>
/// запрещены, потому что host filtering должен принимать только явно перечисленные хосты.
/// </remarks>
public sealed record StartupSettings
{
    private const string ALLOWED_HOSTS_KEY = "AllowedHosts";
    private const string SHUTDOWN_TIMEOUT_SECONDS_KEY = "Deployment:ShutdownTimeoutSeconds";
    private const string HEALTH_DATABASE_TIMEOUT_SECONDS_KEY = "HealthChecks:DatabaseTimeoutSeconds";
    private const string REFRESH_TOKEN_CLEANUP_INTERVAL_MINUTES_KEY = "RefreshTokens:CleanupIntervalMinutes";
    private const string EMAIL_OUTBOX_ENABLED_KEY = "EmailOutbox:Enabled";
    private const string EMAIL_OUTBOX_BATCH_SIZE_KEY = "EmailOutbox:BatchSize";
    private const string EMAIL_OUTBOX_INTERVAL_SECONDS_KEY = "EmailOutbox:IntervalSeconds";
    private const string EMAIL_OUTBOX_MAX_ATTEMPTS_KEY = "EmailOutbox:MaxAttempts";
    private const string EMAIL_OUTBOX_RETENTION_DAYS_KEY = "EmailOutbox:RetentionDays";
    private const string PUSH_ENABLED_KEY = "Push:Enabled";
    private const string PUSH_INTERVAL_SECONDS_KEY = "Push:IntervalSeconds";
    private const string PUSH_BATCH_SIZE_KEY = "Push:BatchSize";
    private const string PUSH_REMINDER_LEAD_MINUTES_KEY = "Push:ReminderLeadMinutes";
    private const string PUSH_MAX_ATTEMPTS_KEY = "Push:MaxAttempts";
    private const string PUSH_RETENTION_DAYS_KEY = "Push:RetentionDays";
    private const string PUSH_DEVICE_TOKEN_RETENTION_DAYS_KEY = "Push:DeviceTokenRetentionDays";
    private const string PUSH_MAX_TOKENS_PER_USER_KEY = "Push:MaxTokensPerUser";
    private const string PUSH_PROJECT_ID_KEY = "Push:ProjectId";
    private const string PUSH_SERVICE_ACCOUNT_JSON_KEY = "Push:ServiceAccountJson";
    private const string PUSH_SERVICE_ACCOUNT_PATH_KEY = "Push:ServiceAccountPath";
    private const string ADDRESS_SUGGESTIONS_ENABLED_KEY = "AddressSuggestions:Enabled";
    private const string ADDRESS_SUGGESTIONS_BASE_URL_KEY = "AddressSuggestions:BaseUrl";
    private const string ADDRESS_SUGGESTIONS_API_KEY_KEY = "AddressSuggestions:ApiKey";
    private const string ADDRESS_SUGGESTIONS_TIMEOUT_SECONDS_KEY = "AddressSuggestions:TimeoutSeconds";
    private const string ADDRESS_SUGGESTIONS_MIN_QUERY_LENGTH_KEY = "AddressSuggestions:MinQueryLength";
    private const string ADDRESS_SUGGESTIONS_MIN_CITY_QUERY_LENGTH_KEY =
        "AddressSuggestions:MinCityQueryLength";
    private const string ADDRESS_SUGGESTIONS_MAX_SUGGESTIONS_KEY = "AddressSuggestions:MaxSuggestions";
    private const string ADDRESS_SUGGESTIONS_CACHE_TTL_SECONDS_KEY = "AddressSuggestions:CacheTtlSeconds";

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
    /// Интервал фоновой уборки истёкших refresh-токенов в минутах (R-50), <c>1..1440</c>.
    /// </summary>
    /// <remarks>
    /// Уборка удаляет строки, истёкшие более <c>RefreshToken.MAX_LIFETIME_DAYS</c> назад;
    /// интервал меняет только частоту запуска, но не границу удаления. Частое значение
    /// уменьшает разовый объём DELETE, редкое — нагрузку на БД.
    /// </remarks>
    public required int RefreshTokenCleanupIntervalMinutes { get; init; }

    /// <summary>
    /// Проверенные настройки outbox писем: воркер, пачка, интервал, попытки и ретенция.
    /// </summary>
    /// <remarks>
    /// Пустая или выключенная секция — нормальное состояние: старт не падает, обработчики
    /// продолжают писать письма в таблицу, а воркер только ждёт включения флага.
    /// </remarks>
    public required EmailOutboxOptions EmailOutbox { get; init; }

    /// <summary>
    /// Проверенные настройки push-канала: воркер, пачка, окно напоминания, попытки и ретенции.
    /// </summary>
    /// <remarks>
    /// Проверяются только числовые диапазоны и флаг. Сам сервисный ключ и project id
    /// не валидируются: приложение обязано собираться и работать без credentials
    /// (<c>Push:Enabled=false</c> или режим разработки), а «битый ключ» — выключенный канал,
    /// а не падение старта.
    /// </remarks>
    public required PushOptions Push { get; init; }

    /// <summary>
    /// Проверенные настройки подсказок города и адреса: флаг, адрес провайдера, ключ и пределы.
    /// </summary>
    /// <remarks>
    /// Пустая или выключенная секция — нормальное состояние, а не ошибка конфигурации:
    /// приложение обязано собираться и стартовать без ключа внешнего провайдера, как с пустыми
    /// секциями <c>Telegram</c> и <c>Push</c>. Выключенная фича не роняет старт: обработчики
    /// не вызывают провайдера и сразу отвечают <c>409 addressSuggestions.unavailable</c>, а форма
    /// салона остаётся набором обычных текстовых полей. Значение
    /// <see cref="AddressSuggestionsOptions.ApiKey"/> никогда не проверяется на «правильность»
    /// и не попадает ни в одно сообщение: при включённой фиче требуется лишь непустой ключ.
    /// </remarks>
    public required AddressSuggestionsOptions AddressSuggestions { get; init; }

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
                Connection string 'Postgres' is not configured: appsettings.json intentionally
                leaves it empty, so no working credentials are stored in the repository.
                For local development, run from the BarberSearch/src/BarberSearch.Web directory:
                  dotnet user-secrets set "ConnectionStrings:Postgres" "<connection string>"
                For deployed environments, set the environment variable ConnectionStrings__Postgres.
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

        // Host filtering не должен пропускать произвольный Host в боевом окружении:
        // wildcard ломает привязку cookie и открывает host-header атаки.
        if (environment.IsProduction())
        {
            ValidateAllowedHosts(configuration);
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
                SHUTDOWN_TIMEOUT_SECONDS_KEY,
                defaultValue: 30,
                min: 5,
                max: 300),
            HealthDatabaseTimeoutSeconds = ReadInt(
                configuration,
                HEALTH_DATABASE_TIMEOUT_SECONDS_KEY,
                defaultValue: 5,
                min: 1,
                max: 60),
            RefreshTokenCleanupIntervalMinutes = ReadInt(
                configuration,
                REFRESH_TOKEN_CLEANUP_INTERVAL_MINUTES_KEY,
                defaultValue: 60,
                min: 1,
                max: 1440),
            EmailOutbox = new EmailOutboxOptions
            {
                // Включение воркера — необязательный флаг: пустая строка трактуется как false.
                Enabled = bool.TryParse(configuration[EMAIL_OUTBOX_ENABLED_KEY], out var enabled)
                          && enabled,
                BatchSize = ReadInt(
                    configuration,
                    EMAIL_OUTBOX_BATCH_SIZE_KEY,
                    EmailOutboxOptions.DEFAULT_BATCH_SIZE,
                    min: 1,
                    max: 1000),
                IntervalSeconds = ReadInt(
                    configuration,
                    EMAIL_OUTBOX_INTERVAL_SECONDS_KEY,
                    EmailOutboxOptions.DEFAULT_INTERVAL_SECONDS,
                    min: 1,
                    max: 3600),
                MaxAttempts = ReadInt(
                    configuration,
                    EMAIL_OUTBOX_MAX_ATTEMPTS_KEY,
                    EmailOutboxOptions.DEFAULT_MAX_ATTEMPTS,
                    min: 1,
                    max: 100),
                RetentionDays = ReadInt(
                    configuration,
                    EMAIL_OUTBOX_RETENTION_DAYS_KEY,
                    EmailOutboxOptions.DEFAULT_RETENTION_DAYS,
                    min: 1,
                    max: 365)
            },
            Push = ReadPushOptions(configuration),
            AddressSuggestions = ReadAddressSuggestions(configuration, environment)
        };
    }

    /// <summary>
    /// Читает и проверяет секцию <c>Push</c>.
    /// </summary>
    /// <remarks>
    /// Числовые диапазоны проверяются здесь, секрет — нет. Дополнительно требуется, чтобы
    /// окно напоминания было не короче одного тика: иначе тик мог бы не попасть ни в одно
    /// окно, и напоминание не создалось бы никогда.
    /// </remarks>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <exception cref="InvalidOperationException">Значение вне диапазона или окно короче тика.</exception>
    private static PushOptions ReadPushOptions(IConfiguration configuration)
    {
        var options = new PushOptions
        {
            // Включение воркера — необязательный флаг; пустая строка трактуется как false.
            Enabled = bool.TryParse(configuration[PUSH_ENABLED_KEY], out var pushEnabled)
                      && pushEnabled,
            IntervalSeconds = ReadInt(
                configuration,
                PUSH_INTERVAL_SECONDS_KEY,
                PushOptions.DEFAULT_INTERVAL_SECONDS,
                min: 1,
                max: 3600),
            BatchSize = ReadInt(
                configuration,
                PUSH_BATCH_SIZE_KEY,
                PushOptions.DEFAULT_BATCH_SIZE,
                min: 1,
                max: 1000),
            ReminderLeadMinutes = ReadInt(
                configuration,
                PUSH_REMINDER_LEAD_MINUTES_KEY,
                PushOptions.DEFAULT_REMINDER_LEAD_MINUTES,
                min: 1,
                max: 1440),
            MaxAttempts = ReadInt(
                configuration,
                PUSH_MAX_ATTEMPTS_KEY,
                PushOptions.DEFAULT_MAX_ATTEMPTS,
                min: 1,
                max: 100),
            RetentionDays = ReadInt(
                configuration,
                PUSH_RETENTION_DAYS_KEY,
                PushOptions.DEFAULT_RETENTION_DAYS,
                min: 1,
                max: 365),
            DeviceTokenRetentionDays = ReadInt(
                configuration,
                PUSH_DEVICE_TOKEN_RETENTION_DAYS_KEY,
                PushOptions.DEFAULT_DEVICE_TOKEN_RETENTION_DAYS,
                min: 1,
                max: 365),
            MaxTokensPerUser = ReadInt(
                configuration,
                PUSH_MAX_TOKENS_PER_USER_KEY,
                PushOptions.DEFAULT_MAX_TOKENS_PER_USER,
                min: 1,
                max: 100),
            // Секреты не проверяются и не логируются: пустое значение — штатное состояние.
            ProjectId = configuration[PUSH_PROJECT_ID_KEY],
            ServiceAccountJson = configuration[PUSH_SERVICE_ACCOUNT_JSON_KEY],
            ServiceAccountPath = configuration[PUSH_SERVICE_ACCOUNT_PATH_KEY]
        };

        if (options.ReminderLeadMinutes * 60 < options.IntervalSeconds)
        {
            throw new InvalidOperationException(
                "Push:ReminderLeadMinutes must cover at least one Push:IntervalSeconds tick: "
                + $"{options.ReminderLeadMinutes} minutes is shorter than "
                + $"{options.IntervalSeconds} seconds.");
        }

        return options;
    }

    /// <summary>
    /// Читает и проверяет секцию <c>AddressSuggestions</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Диапазоны проверяются всегда, секрет — только при включённой фиче. Выключенная фича
    /// с пустым ключом и незаполненным адресом — штатное состояние: подсказки — прогрессивное
    /// улучшение, и их отсутствие не должно мешать старту. Проверки включаются вместе с флагом
    /// <c>AddressSuggestions:Enabled=true</c>, потому что иначе вызов ушёл бы провайдеру
    /// с пустым <c>Authorization</c>, а платная квота тратилась бы на заведомо неудачные запросы.
    /// </para>
    /// <para>
    /// Схема адреса зависит от окружения: в <c>Production</c> принимается только <c>https</c>.
    /// Ключ провайдера уходит в заголовке <c>Authorization</c>, и по открытому <c>http</c> его
    /// прочитал бы любой узел на пути; для платного внешнего API это недопустимо. В
    /// <c>Development</c> и <c>Testing</c> <c>http</c> разрешён: локальный стаб подсказок
    /// поднимается без TLS, и запрет сделал бы проверку контракта без интернета невозможной.
    /// </para>
    /// </remarks>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение хоста: от него зависит разрешённая схема <c>BaseUrl</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// Включённая фича без ключа, <c>BaseUrl</c> не абсолютный URI или <c>http</c> в
    /// <c>Production</c>; значение вне диапазона.
    /// </exception>
    private static AddressSuggestionsOptions ReadAddressSuggestions(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Флаг необязателен: пустая строка, как и у Push, трактуется как false.
        var configuredBaseUrl = configuration[ADDRESS_SUGGESTIONS_BASE_URL_KEY];
        var options = new AddressSuggestionsOptions
        {
            Enabled = bool.TryParse(configuration[ADDRESS_SUGGESTIONS_ENABLED_KEY], out var enabled)
                      && enabled,
            BaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? AddressSuggestionsOptions.DEFAULT_BASE_URL
                : configuredBaseUrl,
            // Секрет не проверяется и не логируется: пустое значение при выключенной фиче — норма.
            ApiKey = configuration[ADDRESS_SUGGESTIONS_API_KEY_KEY],
            TimeoutSeconds = ReadInt(
                configuration,
                ADDRESS_SUGGESTIONS_TIMEOUT_SECONDS_KEY,
                AddressSuggestionsOptions.DEFAULT_TIMEOUT_SECONDS,
                min: 1,
                max: 15),
            MinQueryLength = ReadInt(
                configuration,
                ADDRESS_SUGGESTIONS_MIN_QUERY_LENGTH_KEY,
                AddressSuggestionsOptions.DEFAULT_MIN_QUERY_LENGTH,
                min: 2,
                max: 10),
            MinCityQueryLength = ReadInt(
                configuration,
                ADDRESS_SUGGESTIONS_MIN_CITY_QUERY_LENGTH_KEY,
                AddressSuggestionsOptions.DEFAULT_MIN_CITY_QUERY_LENGTH,
                min: 1,
                max: 10),
            MaxSuggestions = ReadInt(
                configuration,
                ADDRESS_SUGGESTIONS_MAX_SUGGESTIONS_KEY,
                AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS,
                min: 1,
                max: 10),
            CacheTtlSeconds = ReadInt(
                configuration,
                ADDRESS_SUGGESTIONS_CACHE_TTL_SECONDS_KEY,
                AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS,
                min: 0,
                max: 86400)
        };

        if (!options.Enabled)
        {
            return options;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                """
                AddressSuggestions:ApiKey is required when AddressSuggestions:Enabled=true.
                Set the environment variable AddressSuggestions__ApiKey (or user secrets in Development).
                The key is never stored in appsettings.json and never logged.
                """);
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                "AddressSuggestions:BaseUrl must be an absolute URI, e.g. "
                + AddressSuggestionsOptions.DEFAULT_BASE_URL
                + ". The configured value is not printed.");
        }

        // Production требует https: ключ провайдера уходит в заголовке Authorization.
        // В Development/Testing http разрешён — там поднимают локальный стаб подсказок.
        if (environment.IsProduction()
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AddressSuggestions:BaseUrl must use https in Production: "
                + "the provider API key travels in a request header, and plain http "
                + "would expose it to every hop. http is accepted only in Development/Testing "
                + "for a local stub.");
        }

        return options;
    }

    /// <summary>
    /// Проверяет список разрешённых хостов для боевого окружения.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core host filtering читает <c>AllowedHosts</c> напрямую, поэтому пустое
    /// значение или <c>*</c> нужно отклонить до запуска хоста. В Development/Testing
    /// проверка не выполняется: локальный хост и тестовый <c>TestServer</c> остаются
    /// работоспособными.
    /// </remarks>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <exception cref="InvalidOperationException">
    /// Список пуст или содержит wildcard <c>*</c>.
    /// </exception>
    private static void ValidateAllowedHosts(IConfiguration configuration)
    {
        var hosts = (configuration[ALLOWED_HOSTS_KEY] ?? string.Empty).Split(
            ';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (hosts.Length == 0)
        {
            throw new InvalidOperationException(
                """
                AllowedHosts is not configured for Production.
                List the public host names semicolon-separated, without a scheme or path, e.g.
                  AllowedHosts=api.example.com
                Host filtering answers 400 to an unknown Host header instead of serving the API.
                """);
        }

        if (hosts.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                """
                AllowedHosts='*' is not allowed outside local development: a wildcard accepts
                any Host header and weakens host-header and cookie-scoping protections.
                List the public host names semicolon-separated, without a scheme or path, e.g.
                  AllowedHosts=api.example.com
                Host filtering answers 400 to an unknown Host header instead of serving the API.
                """);
        }
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
