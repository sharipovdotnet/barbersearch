using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// <c>RememberMe</c> в cookie-ветке refresh: браузер сохраняет access-cookie между
/// перезапусками только по явной просьбе, отсутствие поля — сессионная cookie.
/// </summary>
/// <remarks>
/// Атрибут <c>expires</c> разбирается у конкретного <c>Set-Cookie</c> по имени access-cookie:
/// у refresh-cookie он есть всегда, поэтому поиск подстроки по всем заголовкам ответа
/// доказывал бы не то. Тело запроса собирается как JSON-объект, а не контрактом: так тест
/// проверяет HTTP-поведение и остаётся красным до появления поля в контракте.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RefreshRememberMeTests
{
    /// <summary>
    /// Имя access-cookie вне Development (Testing — не Development).
    /// </summary>
    private const string ACCESS_COOKIE_NAME = "__Host-barbersearch";

    private readonly PostgresFixture _database;

    public RefreshRememberMeTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Refresh_with_remember_me_true_sets_persistent_access_cookie()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"remember-true-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email, rememberMe: false);

        using var response = await RefreshAsync(session, new { rememberMe = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var accessCookie = ReadAccessCookie(response);

        Assert.True(
            HasAttribute(accessCookie, "expires="),
            $"Refresh с rememberMe: true должен ставить persistent access-cookie. Set-Cookie: {accessCookie}");
    }

    [Fact(Timeout = 180_000)]
    public async Task Refresh_with_remember_me_false_sets_session_access_cookie()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"remember-false-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email, rememberMe: true);

        using var response = await RefreshAsync(session, new { rememberMe = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var accessCookie = ReadAccessCookie(response);

        Assert.False(
            HasAttribute(accessCookie, "expires="),
            $"Refresh с rememberMe: false должен ставить session-cookie. Set-Cookie: {accessCookie}");
    }

    [Fact(Timeout = 180_000)]
    public async Task Refresh_without_remember_me_field_sets_session_access_cookie()
    {
        await using var app = await TestApp.StartAsync(_database);
        var email = $"remember-absent-{Guid.NewGuid():N}@barbersearch.test";
        await app.CreateUserAsync(email);

        await using var session = await app.SignInWithCookieAsync(email, rememberMe: true);

        using var response = await RefreshAsync(session, body: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var accessCookie = ReadAccessCookie(response);

        Assert.False(
            HasAttribute(accessCookie, "expires="),
            $"Refresh без rememberMe должен ставить session-cookie. Set-Cookie: {accessCookie}");
    }

    private static async Task<HttpResponseMessage> RefreshAsync(
        CookieSession session,
        object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);

        return await session.Client.SendAsync(request);
    }

    private static string ReadAccessCookie(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var values),
            $"Ответ {response.StatusCode} должен содержать Set-Cookie.");

        var accessCookie = values!
            .FirstOrDefault(setCookie => setCookie.StartsWith(
                $"{ACCESS_COOKIE_NAME}=",
                StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(accessCookie),
            $"Ответ refresh должен ставить cookie {ACCESS_COOKIE_NAME}. Получены: {string.Join(" | ", values!)}");

        return accessCookie!;
    }

    private static bool HasAttribute(string setCookie, string attribute)
        => setCookie
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(segment => segment.StartsWith(attribute, StringComparison.OrdinalIgnoreCase));
}
