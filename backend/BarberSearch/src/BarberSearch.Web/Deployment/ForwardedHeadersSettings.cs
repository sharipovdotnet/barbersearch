using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки обработки <c>X-Forwarded-For</c> и <c>X-Forwarded-Proto</c>.
/// </summary>
/// <remarks>
/// Заголовкам доверяет только явно перечисленный прокси. Если <see cref="Enabled"/> включён,
/// список <see cref="KnownProxies"/> / <see cref="KnownNetworks"/> обязателен: иначе приложение
/// молча игнорировало бы настоящий адрес клиента, и rate limit делился бы на всех.
/// <para>
/// <c>X-Forwarded-Host</c> намеренно не обрабатывается: доверять Host из заголовка без
/// отдельной необходимости не нужно.
/// </para>
/// </remarks>
public sealed record ForwardedHeadersSettings
{
    /// <summary>
    /// Имя секции конфигурации.
    /// </summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Включает middleware. По умолчанию выключен: без прокси заголовки не нужны.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Сколько прокси в цепочке обрабатывать. Для одного балансировщика — 1.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;

    /// <summary>
    /// IP-адреса доверенных прокси.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>
    /// Доверенные сети в формате CIDR, например <c>10.0.0.0/8</c>.
    /// </summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Читает и проверяет настройки.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Middleware включён без доверенных прокси/сетей или адрес имеет неверный формат.
    /// </exception>
    public static ForwardedHeadersSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration
            .GetSection(SectionName)
            .Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

        if (!settings.Enabled)
        {
            return settings with { KnownProxies = [], KnownNetworks = [] };
        }

        if (settings.ForwardLimit is < 1 or > 10)
        {
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be between 1 and 10.");
        }

        var proxies = Normalize(settings.KnownProxies);
        var networks = Normalize(settings.KnownNetworks);

        if (proxies.Length == 0 && networks.Length == 0)
        {
            throw new InvalidOperationException(
                """
                ForwardedHeaders is enabled, but no trusted proxy is configured.
                Set ForwardedHeaders__KnownProxies__0 (or KnownNetworks__0) to your proxy address.
                Never trust forwarded headers from arbitrary clients.
                """);
        }

        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies entry '{proxy}' is not a valid IP address.");
            }
        }

        foreach (var network in networks)
        {
            try
            {
                IPNetwork.Parse(network);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks entry '{network}' is not a valid CIDR network.");
            }
        }

        return settings with { KnownProxies = proxies, KnownNetworks = networks };
    }

    /// <summary>
    /// Превращает настройки в параметры ASP.NET Core middleware.
    /// </summary>
    public ForwardedHeadersOptions ToOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = ForwardLimit
        };

        // Убираем неявный loopback: доверяем только тому, что перечислено в конфигурации.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(IPNetwork.Parse(network));
        }

        return options;
    }

    private static string[] Normalize(string[]? values)
        => values is null
            ? []
            : values
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToArray();
}
