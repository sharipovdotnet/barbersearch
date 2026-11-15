namespace BarberSearch.Mobile.Core.Configuration;

/// <summary>
/// Адрес локального бэкенда в отладочной сборке: правило по устройству и адреса из хоста.
/// </summary>
/// <remarks>
/// <para>
/// Эмулятор Android видит машину разработчика как <c>10.0.2.2</c>, симулятор iOS — как
/// <c>localhost</c>. Реальный телефон не видит ни того, ни другого: <c>10.0.2.2</c> на нём
/// указывает на сам телефон, а адрес машины приложению неизвестен. Поэтому телефон ходит на
/// <c>localhost</c>, который на компьютер пробрасывает <c>adb reverse tcp:5243 tcp:5243</c>:
/// адрес не зависит от IP-адреса машины и её брандмауэра и меняется одной командой на каждое
/// подключение, а не правкой кода. Явно заданный хост важнее правила — он закрывает второй
/// сценарий, телефон в той же сети и сборку без проброса портов.
/// </para>
/// <para>
/// Core не знает ни про MAUI, ни про MSBuild: и платформа, и хост приходят из композиционного
/// корня приложения, как в <see cref="Api.BarberSearchApiOptions"/> и
/// <see cref="Legal.LegalOptions"/>.
/// </para>
/// </remarks>
public static class DevelopmentEndpoints
{
    /// <summary>Порт локального бэкенда.</summary>
    public const int ApiPort = 5243;

    /// <summary>Порт веб-сайта: правовые документы открываются в браузере, а не в приложении.</summary>
    public const int SitePort = 5173;

    /// <summary>Адрес машины разработчика внутри эмулятора Android.</summary>
    private const string EmulatorHost = "10.0.2.2";

    /// <summary>Адрес, который на компьютер пробрасывает <c>adb reverse</c>.</summary>
    private const string ForwardedHost = "localhost";

    /// <summary>
    /// Выбирает хост локального бэкенда: явно заданный важнее правила по устройству.
    /// </summary>
    /// <remarks>
    /// Пустая строка означает «не задано», а не ошибку: параметр сборки может прийти пустым,
    /// и адрес тогда обязан остаться рабочим по умолчанию.
    /// </remarks>
    /// <param name="configuredHost">Хост из сборки (<c>-p:DevApiHost</c>) или <c>null</c>.</param>
    /// <param name="androidPlatform">Приложение собрано для Android.</param>
    /// <param name="virtualDevice">Приложение запущено в эмуляторе или симуляторе.</param>
    /// <returns>Хост без схемы и порта.</returns>
    /// <exception cref="ArgumentException">
    /// Заданная строка — не имя хоста и не IP-адрес (схема, порт или путь).
    /// </exception>
    public static string ResolveHost(string? configuredHost, bool androidPlatform, bool virtualDevice)
    {
        if (string.IsNullOrWhiteSpace(configuredHost))
        {
            return androidPlatform && virtualDevice ? EmulatorHost : ForwardedHost;
        }

        return EnsureHost(configuredHost);
    }

    /// <summary>
    /// Собирает базовый адрес API с завершающим слэшем.
    /// </summary>
    /// <param name="host">Хост без схемы и порта.</param>
    /// <exception cref="ArgumentException">Хост пуст или не является именем хоста или IP-адресом.</exception>
    public static string ApiBaseAddress(string host) => BaseAddress(host, ApiPort);

    /// <summary>
    /// Собирает базовый адрес сайта с завершающим слэшем.
    /// </summary>
    /// <param name="host">Хост без схемы и порта.</param>
    /// <exception cref="ArgumentException">Хост пуст или не является именем хоста или IP-адресом.</exception>
    public static string SiteBaseAddress(string host) => BaseAddress(host, SitePort);

    private static string BaseAddress(string? host, int port)
    {
        var authority = EnsureHost(host);

        // IPv6-адрес в URL заключается в скобки: иначе двоеточия слились бы с портом.
        if (authority.Contains(':', StringComparison.Ordinal))
        {
            authority = string.Concat("[", authority, "]");
        }

        return $"http://{authority}:{port}/";
    }

    private static string EnsureHost(string? host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var trimmed = host.Trim();

        if (Uri.CheckHostName(trimmed) == UriHostNameType.Unknown)
        {
            throw new ArgumentException(
                "The development host must be a host name or IP address without scheme, port or path.",
                nameof(host));
        }

        return trimmed;
    }
}
