using System.Net;
using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Csrf;
using BarberSearch.Contracts.Auth.Login;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Атрибуты refresh-cookie браузерной ветки (R-49): префикс <c>__Host-</c> и вытекающий из него
/// <c>Path=/</c>, который намеренно не сужается до <c>/api/auth</c>.
/// </summary>
/// <remarks>
/// Cookie проверяется по сырому <c>Set-Cookie</c> ответа <c>POST api/auth/login/cookie</c>, а не
/// по jar клиента: <c>Secure</c> в Testing включён всегда, поэтому cookie-контейнер не отправил бы
/// её обратно по http, а атрибуты — ровно то, что видит браузер. <c>Path</c> сравнивается сегментом
/// целиком: <c>Contains("path=/")</c> прошёл бы и на <c>path=/api/auth</c>, то есть не поймал бы
/// сужение пути и потерю <c>__Host-</c>. Отсутствие <c>Domain</c> проверяется отдельно — иначе
/// браузер отбросил бы cookie с префиксом <c>__Host-</c>.
/// <para>
/// Оба запроса идут по абсолютному https-адресу: вне Development antiforgery-политика требует
/// <c>Secure</c> (<c>SecurePolicy=Always</c>), а <c>GetAndStoreTokens</c> на не-SSL запросе
/// бросает <see cref="InvalidOperationException"/>. TLS TestServer не терминирует — схема нужна
/// только для <c>Request.IsHttps</c>.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RefreshCookieTests
{
    /// <summary>
    /// Имя refresh-cookie вне Development: Testing — не Development, поэтому имя production.
    /// </summary>
    private const string REFRESH_COOKIE_NAME = "__Host-barbersearch-refresh";

    /// <summary>
    /// Имя access-cookie вне Development.
    /// </summary>
    private const string ACCESS_COOKIE_NAME = "__Host-barbersearch";

    /// <summary>
    /// Абсолютный https-адрес выдачи antiforgery-токена: в Testing обязателен <c>Secure</c>.
    /// </summary>
    private const string CSRF_URL = "https://localhost/api/auth/csrf";

    /// <summary>
    /// Абсолютный https-адрес cookie-входа — по той же причине, что и <see cref="CSRF_URL"/>.
    /// </summary>
    private const string LOGIN_COOKIE_URL = "https://localhost/api/auth/login/cookie";

    private readonly PostgresFixture _database;

    public RefreshCookieTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Login_cookie_sets_host_prefixed_refresh_cookie_with_root_path()
    {
        await using var app = await TestApp.StartAsync(_database);

        var email = $"refresh-cookie-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        using var csrfResponse = await app.Client.GetAsync(CSRF_URL);
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);

        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.NotNull(csrf);
        Assert.False(string.IsNullOrWhiteSpace(csrf.Token));

        var antiforgeryCookie = ReadSetCookies(csrfResponse)
            .Select(setCookie => setCookie.Split(';', 2)[0].Trim())
            .FirstOrDefault(pair => pair.Contains('=', StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(antiforgeryCookie),
            "GET api/auth/csrf должен ставить antiforgery-cookie: без пары cookie+токен логин не пройдёт.");

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, LOGIN_COOKIE_URL)
        {
            Content = JsonContent.Create(new LoginRequest(email, TestApp.Password))
        };
        loginRequest.Headers.Add("Cookie", antiforgeryCookie!);
        loginRequest.Headers.Add("X-CSRF-TOKEN", csrf.Token);

        using var loginResponse = await app.Client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

        var cookies = ReadSetCookies(loginResponse);

        var refreshCookie = cookies.FirstOrDefault(
            setCookie => setCookie.StartsWith($"{REFRESH_COOKIE_NAME}=", StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(refreshCookie),
            $"Ответ login/cookie должен ставить cookie {REFRESH_COOKIE_NAME}. Получены: {string.Join(" | ", cookies)}");

        var refreshSegments = ParseSegments(refreshCookie!);

        var (refreshName, refreshValue) = SplitPair(refreshSegments[0]);
        Assert.Equal(REFRESH_COOKIE_NAME, refreshName);
        Assert.False(string.IsNullOrEmpty(refreshValue), "Refresh-cookie должна нести токен в значении.");

        // R-49: path=/ — сегмент целиком, потому что префикс требует именно корень.
        AssertCookieAttribute(refreshSegments, refreshCookie!, "path=/");
        AssertCookieAttribute(refreshSegments, refreshCookie!, "httponly");
        AssertCookieAttribute(refreshSegments, refreshCookie!, "secure");
        AssertCookieAttribute(refreshSegments, refreshCookie!, "samesite=lax");

        Assert.False(
            refreshSegments.Any(segment => segment.StartsWith("domain=", StringComparison.OrdinalIgnoreCase)),
            $"Cookie с префиксом __Host- не должна иметь Domain. Set-Cookie: {refreshCookie}");

        // Access-cookie того же ответа — тот же корневой путь; проверка дешёвая, но ловит
        // расхождение двух cookie, которые ставит один login.
        var accessCookie = cookies.FirstOrDefault(
            setCookie => setCookie.StartsWith($"{ACCESS_COOKIE_NAME}=", StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(accessCookie),
            $"Ответ login/cookie должен ставить cookie {ACCESS_COOKIE_NAME}. Получены: {string.Join(" | ", cookies)}");

        var accessSegments = ParseSegments(accessCookie!);

        AssertCookieAttribute(accessSegments, accessCookie!, "path=/");
    }

    /// <summary>
    /// Возвращает все значения <c>Set-Cookie</c> ответа в том виде, в каком их увидит браузер.
    /// </summary>
    private static List<string> ReadSetCookies(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var values),
            $"Ответ {response.StatusCode} должен содержать заголовок Set-Cookie.");

        return values!.ToList();
    }

    /// <summary>
    /// Делит <c>Set-Cookie</c> на сегменты без пробелов: первый — <c>name=value</c>, дальше атрибуты.
    /// </summary>
    private static string[] ParseSegments(string setCookie)
        => setCookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Делит сегмент <c>name=value</c> по первому <c>=</c>: значение токена может содержать <c>=</c>.
    /// </summary>
    private static (string Name, string Value) SplitPair(string segment)
    {
        var parts = segment.Split('=', 2);

        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], string.Empty);
    }

    /// <summary>
    /// Требует сегмент, равный атрибуту целиком (без учёта регистра), а не содержащий его подстрокой.
    /// </summary>
    private static void AssertCookieAttribute(string[] segments, string setCookie, string attribute)
    {
        Assert.True(
            segments.Any(segment => string.Equals(segment, attribute, StringComparison.OrdinalIgnoreCase)),
            $"Cookie должна содержать атрибут '{attribute}' целиком. Set-Cookie: {setCookie}");
    }
}
