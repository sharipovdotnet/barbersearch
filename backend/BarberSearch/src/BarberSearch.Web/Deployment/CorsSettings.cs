namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки CORS из секции <c>Cors</c>.
/// </summary>
/// <remarks>
/// Список источников задаётся явно. <c>*</c> запрещён: политика всегда включает
/// <c>AllowCredentials</c>, а браузеры не разрешают wildcard-origin вместе с credentials.
/// Пустой список означает, что CORS не регистрируется: тот же origin в CORS не нуждается.
/// </remarks>
public sealed record CorsSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "Cors";

    /// <summary>
    /// Имя политики CORS.
    /// </summary>
    public const string PolicyName = "spa";

    /// <summary>
    /// Разрешённые origins без завершающего слэша, например <c>https://app.example.com</c>.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    /// <summary>
    /// Признак того, что CORS настроен.
    /// </summary>
    public bool Enabled => AllowedOrigins.Length > 0;

    /// <summary>
    /// Читает и проверяет список origins.
    /// </summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <exception cref="InvalidOperationException">
    /// В списке есть <c>*</c>, пустое значение, путь, query, fragment или неверная схема.
    /// </exception>
    public static CorsSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration
            .GetSection($"{SectionName}:AllowedOrigins")
            .Get<string[]>() ?? [];

        if (configured.Length == 0)
        {
            return new CorsSettings();
        }

        var origins = new List<string>(configured.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in configured)
        {
            var origin = raw?.Trim() ?? string.Empty;

            if (origin.Length == 0)
            {
                throw new InvalidOperationException("Cors:AllowedOrigins must not contain empty entries.");
            }

            if (origin == "*")
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must contain explicit origins; '*' is not allowed because " +
                    "the policy allows credentials.");
            }

            if (origin.EndsWith('/'))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins entry '{origin}' must not end with '/'.");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins entry '{origin}' is not a valid origin. " +
                    "Use scheme://host[:port] without a path, query, fragment or trailing slash.");
            }

            if (seen.Add(origin))
            {
                origins.Add(origin);
            }
        }

        return new CorsSettings { AllowedOrigins = origins.ToArray() };
    }
}
